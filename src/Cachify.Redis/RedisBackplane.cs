using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cachify.Abstractions;
using Cachify.Core;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cachify.Redis;

/// <summary>
/// Implements a Redis pub/sub backplane for distributed cache invalidation.
/// </summary>
/// <remarks>
/// Design Notes: the backplane is opt-in and uses lightweight JSON messages with versioning
/// to avoid coupling core cache users to Redis.
/// </remarks>
public sealed class RedisBackplane : ICacheBackplane, IAsyncDisposable
{
    private readonly ISubscriber _subscriber;
    private readonly CacheBackplaneOptions _options;
    private readonly ILogger<RedisBackplane> _logger;
    private readonly ConcurrentDictionary<Guid, Func<CacheInvalidation, CancellationToken, Task>> _handlers = new();
    private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly ConcurrentQueue<CacheInvalidation> _batchQueue = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _batchLock = new();
    private Timer? _batchTimer;
    private int _batchCount;
    private int _isSubscribed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisBackplane"/> class.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer.</param>
    /// <param name="options">The Cachify options containing backplane configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public RedisBackplane(
        IConnectionMultiplexer connection,
        CachifyOptions options,
        ILogger<RedisBackplane> logger)
    {
        _subscriber = connection.GetSubscriber();
        _options = options.Backplane;
        _logger = logger;
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Func<CacheInvalidation, CancellationToken, Task> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var id = Guid.NewGuid();
        _handlers[id] = handler;
        _ = EnsureSubscribedAsync();

        return new BackplaneSubscription(() => _handlers.TryRemove(id, out _));
    }

    /// <inheritdoc />
    public async Task PublishInvalidationAsync(CacheInvalidation invalidation, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _disposed)
        {
            return;
        }

        if (_options.BatchSize > 1 || _options.BatchWindow > TimeSpan.Zero)
        {
            var batchCount = EnqueueBatch(invalidation);
            if (_options.BatchSize > 1 && batchCount >= _options.BatchSize)
            {
                await FlushBatchAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await PublishMessageAsync(RedisBackplaneMessage.CreateSingle(invalidation), cancellationToken).ConfigureAwait(false);
    }

    private int EnqueueBatch(CacheInvalidation invalidation)
    {
        lock (_batchLock)
        {
            _batchQueue.Enqueue(invalidation);
            _batchCount++;

            if (_options.BatchWindow > TimeSpan.Zero && _batchTimer is null)
            {
                _batchTimer = new Timer(_ => _ = FlushBatchAsync(CancellationToken.None), null, _options.BatchWindow, Timeout.InfiniteTimeSpan);
            }

            return _batchCount;
        }
    }

    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!await _flushGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            List<CacheInvalidation> batch;
            lock (_batchLock)
            {
                batch = new List<CacheInvalidation>();
                while (_batchQueue.TryDequeue(out var invalidation))
                {
                    batch.Add(invalidation);
                }

                _batchCount = 0;
                DisposeBatchTimer();
            }

            if (batch.Count == 0)
            {
                return;
            }

            var message = RedisBackplaneMessage.CreateBatch(_options.InstanceId, batch);
            await PublishMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private void DisposeBatchTimer()
    {
        _batchTimer?.Dispose();
        _batchTimer = null;
    }

    private async Task PublishMessageAsync(RedisBackplaneMessage message, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _subscriber.PublishAsync(_options.ChannelName, message.Serialize()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cachify Redis backplane publish failed.");
        }
    }

    private async Task EnsureSubscribedAsync()
    {
        if (Volatile.Read(ref _isSubscribed) == 1 || !_options.Enabled)
        {
            return;
        }

        await _subscriptionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isSubscribed == 1 || !_options.Enabled)
            {
                return;
            }

            await _subscriber.SubscribeAsync(_options.ChannelName, HandleMessageAsync).ConfigureAwait(false);
            _isSubscribed = 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cachify Redis backplane subscription failed.");
            _isSubscribed = 0;
        }
        finally
        {
            _subscriptionGate.Release();
        }
    }

    private void HandleMessageAsync(RedisChannel channel, RedisValue payload)
    {
        _ = DispatchInvalidationsAsync(payload);
    }

    private async Task DispatchInvalidationsAsync(RedisValue payload)
    {
        if (_disposeCts.IsCancellationRequested || _disposed)
        {
            return;
        }

        if (!RedisBackplaneMessage.TryDeserialize(payload.ToString(), out var message))
        {
            return;
        }

        foreach (var invalidation in message.ToInvalidations())
        {
            foreach (var handler in _handlers.Values)
            {
                try
                {
                    await handler(invalidation, _disposeCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cachify Redis backplane handler failed.");
                }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposeCts.Cancel();
        lock (_batchLock)
        {
            DisposeBatchTimer();
        }
        await FlushBatchAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _subscriptionGate.Dispose();
        _flushGate.Dispose();
        _disposeCts.Dispose();
    }

    private sealed class BackplaneSubscription : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public BackplaneSubscription(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDispose();
        }
    }
}
