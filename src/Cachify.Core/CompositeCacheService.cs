using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using Cachify.Abstractions;
using Microsoft.Extensions.Logging;

namespace Cachify.Core;

/// <summary>
/// Orchestrates composite cache behavior across memory (L1) and distributed (L2) layers.
/// </summary>
public sealed class CompositeCacheService : ICompositeCacheService, IDisposable
{
    private static readonly Meter Meter = new("Cachify");
    private static readonly Counter<long> CacheHitTotal = Meter.CreateCounter<long>("cache_hit_total");
    private static readonly Counter<long> CacheMissTotal = Meter.CreateCounter<long>("cache_miss_total");
    private static readonly Counter<long> CacheSetTotal = Meter.CreateCounter<long>("cache_set_total");
    private static readonly Counter<long> CacheRemoveTotal = Meter.CreateCounter<long>("cache_remove_total");
    private static readonly Counter<long> BackplaneInvalidationPublishedTotal = Meter.CreateCounter<long>("cache_backplane_invalidation_published_total");
    private static readonly Counter<long> BackplaneInvalidationReceivedTotal = Meter.CreateCounter<long>("cache_backplane_invalidation_received_total");
    private static readonly Histogram<double> CacheGetDuration = Meter.CreateHistogram<double>("cache_get_duration_ms");
    private static readonly Counter<long> StaleServedTotal = Meter.CreateCounter<long>("stale_served_count");
    private static readonly Counter<long> SoftTimeoutTotal = Meter.CreateCounter<long>("factory_timeout_soft_count");
    private static readonly Counter<long> HardTimeoutTotal = Meter.CreateCounter<long>("factory_timeout_hard_count");
    private static readonly Counter<long> FailSafeUsedTotal = Meter.CreateCounter<long>("failsafe_used_count");

    private static readonly ActivitySource ActivitySource = new("Cachify");

    private const string MetadataSuffix = ":meta";

    private readonly IMemoryCacheService? _memory;
    private readonly IDistributedCacheService? _distributed;
    private readonly ICacheBackplane? _backplane;
    private readonly CachifyOptions _options;
    private readonly ICacheKeyBuilder _keyBuilder;
    private readonly CacheStampedeGuard _guard;
    private readonly ILogger<CompositeCacheService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, Task> _refreshTasks = new();
    private readonly CacheBackplaneOptions _backplaneOptions;
    private readonly IDisposable? _backplaneSubscription;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeCacheService"/> class.
    /// </summary>
    /// <param name="memory">The optional L1 cache service.</param>
    /// <param name="distributed">The optional L2 cache service.</param>
    /// <param name="options">The global Cachify options.</param>
    /// <param name="keyBuilder">The cache key builder.</param>
    /// <param name="guard">The stampede guard.</param>
    /// <param name="logger">The logger instance.</param>
    public CompositeCacheService(
        IMemoryCacheService? memory,
        IDistributedCacheService? distributed,
        ICacheBackplane? backplane,
        CachifyOptions options,
        ICacheKeyBuilder keyBuilder,
        CacheStampedeGuard guard,
        ILogger<CompositeCacheService> logger)
    {
        _memory = memory;
        _distributed = distributed;
        _backplane = backplane;
        _options = options;
        _keyBuilder = keyBuilder;
        _guard = guard;
        _logger = logger;
        _timeProvider = options.TimeProvider;
        _backplaneOptions = options.Backplane;

        if (_backplane is not null && _backplaneOptions.Enabled)
        {
            _backplaneSubscription = _backplane.Subscribe(HandleBackplaneInvalidationAsync);
        }
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildKey(key, null);
        using var activity = ActivitySource.StartActivity("cachify.get", ActivityKind.Internal);
        var readResult = await GetInternalAsync<T>(cacheKey, cancellationToken, activity).ConfigureAwait(false);
        if (readResult.IsStale)
        {
            RecordStale(activity, readResult.StaleReason, refreshScheduled: false);
        }

        return readResult.Value;
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildKey(key, options);
        using var activity = ActivitySource.StartActivity("cachify.set", ActivityKind.Internal);

        var resolvedOptions = ResolveEntryOptions(options);
        var resilience = ResolveResilienceOptions(options);
        await SetEntryAsync(cacheKey, value, resolvedOptions, resilience, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildKey(key, null);
        using var activity = ActivitySource.StartActivity("cachify.remove", ActivityKind.Internal);
        var metadataKey = BuildMetadataKey(cacheKey);

        if (_distributed is not null)
        {
            try
            {
                await _distributed.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
                await _distributed.RemoveAsync(metadataKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cachify L2 remove failed for {Key}", cacheKey);
                if (_options.FailFastOnL2Errors)
                {
                    throw;
                }
            }
        }

        if (_memory is not null)
        {
            await _memory.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            await _memory.RemoveAsync(metadataKey, cancellationToken).ConfigureAwait(false);
        }

        await PublishInvalidationAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        CacheRemoveTotal.Add(1);
    }

    /// <inheritdoc />
    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildKey(key, options);
        using var activity = ActivitySource.StartActivity("cachify.getorset", ActivityKind.Internal);
        var resolvedOptions = ResolveEntryOptions(options);
        var resilience = ResolveResilienceOptions(options);
        var firstRead = await GetInternalAsync<T>(cacheKey, cancellationToken).ConfigureAwait(false);

        if (firstRead.IsFresh)
        {
            return firstRead.Value!;
        }

        var staleCandidate = firstRead.IsStale ? firstRead : null;
        var refreshTask = GetOrAddRefreshTask(cacheKey, factory, resolvedOptions, resilience, allowBackground: staleCandidate is not null, cancellationToken);

        if (staleCandidate is not null && resilience.SoftTimeout is not null)
        {
            var softTimeoutResult = await TryAwaitWithSoftTimeoutAsync(refreshTask, resilience.SoftTimeout.Value, cancellationToken).ConfigureAwait(false);
            if (softTimeoutResult.TimedOut)
            {
                SoftTimeoutTotal.Add(1);
                var refreshScheduled = resilience.EnableBackgroundRefresh && !cancellationToken.IsCancellationRequested;
                RecordStale(activity, CacheEntryStaleReason.SoftTimeout, refreshScheduled);
                ScheduleRefresh(cacheKey, factory, resolvedOptions, resilience, cancellationToken);
                return staleCandidate.Value!;
            }

            return softTimeoutResult.Result!;
        }

        try
        {
            return await refreshTask.ConfigureAwait(false);
        }
        catch (TimeoutException) when (staleCandidate is not null)
        {
            HardTimeoutTotal.Add(1);
            var refreshScheduled = resilience.EnableBackgroundRefresh && !cancellationToken.IsCancellationRequested;
            RecordStale(activity, CacheEntryStaleReason.HardTimeout, refreshScheduled);
            ScheduleRefresh(cacheKey, factory, resolvedOptions, resilience, cancellationToken);
            return staleCandidate.Value!;
        }
        catch (TimeoutException)
        {
            HardTimeoutTotal.Add(1);
            activity?.SetTag("cachify.timeout_type", "hard");
            throw;
        }
        catch (Exception) when (staleCandidate is not null)
        {
            var refreshScheduled = resilience.EnableBackgroundRefresh && !cancellationToken.IsCancellationRequested;
            RecordStale(activity, CacheEntryStaleReason.FactoryFailure, refreshScheduled);
            ScheduleRefresh(cacheKey, factory, resolvedOptions, resilience, cancellationToken);
            return staleCandidate.Value!;
        }
    }

    /// <summary>
    /// Attempts to read a cache entry across L1/L2 layers, returning freshness metadata.
    /// </summary>
    /// <remarks>
    /// Design Notes: returns stale entries when within the fail-safe window so callers can decide
    /// how to proceed (serve stale, refresh, or fail).
    /// </remarks>
    private async Task<CacheEntryReadResult<T>> GetInternalAsync<T>(
        string cacheKey,
        CancellationToken cancellationToken,
        Activity? activityOverride = null)
    {
        using var activity = activityOverride is null
            ? ActivitySource.StartActivity("cachify.get", ActivityKind.Internal)
            : null;
        var start = Stopwatch.GetTimestamp();
        CacheEntryReadResult<T>? staleCandidate = null;

        if (_memory is not null)
        {
            var memoryResult = await TryReadEntryAsync(_memory, cacheKey, cancellationToken).ConfigureAwait(false);
            if (memoryResult.IsFresh)
            {
                RecordHit("L1");
                CacheGetDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                return memoryResult;
            }

            if (memoryResult.IsStale)
            {
                staleCandidate = memoryResult;
            }
        }

        if (_distributed is not null)
        {
            try
            {
                var distributedResult = await TryReadEntryAsync(_distributed, cacheKey, cancellationToken).ConfigureAwait(false);
                if (distributedResult.IsFresh)
                {
                    RecordHit("L2");
                    if (_memory is not null)
                    {
                        var memoryOptions = await CopyMetadataAsync(_distributed, _memory, cacheKey, cancellationToken).ConfigureAwait(false);
                        await _memory.SetAsync(cacheKey, distributedResult.Value!, memoryOptions, cancellationToken).ConfigureAwait(false);
                    }

                    CacheGetDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    return distributedResult;
                }

                if (distributedResult.IsStale && staleCandidate is null)
                {
                    staleCandidate = distributedResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cachify L2 get failed for {Key}", cacheKey);
                if (_options.FailFastOnL2Errors && staleCandidate is null)
                {
                    throw;
                }

                if (staleCandidate is not null)
                {
                    CacheGetDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    return new CacheEntryReadResult<T>(staleCandidate.Value!.Value, CacheEntryState.Stale, CacheEntryStaleReason.L2Failure);
                }
            }
        }

        if (staleCandidate is not null)
        {
            RecordHit("stale");
            CacheGetDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return staleCandidate.Value;
        }

        RecordMiss();
        CacheGetDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return new CacheEntryReadResult<T>(default, CacheEntryState.Miss, CacheEntryStaleReason.None);
    }

    /// <summary>
    /// Stores the value along with metadata, extending the TTL for fail-safe usage when enabled.
    /// </summary>
    /// <remarks>
    /// Design Notes: metadata and value share the same TTL to keep logical expiration aligned with
    /// the stored payload when stale fallback is enabled.
    /// </remarks>
    private async Task SetEntryAsync<T>(
        string cacheKey,
        T value,
        CacheEntryOptions options,
        CacheResilienceOptions resilience,
        CancellationToken cancellationToken)
    {
        var metadata = BuildMetadata(options, resilience);
        var metadataKey = BuildMetadataKey(cacheKey);
        var storageOptions = BuildStorageOptions(options, resilience);

        if (_distributed is not null)
        {
            try
            {
                await _distributed.SetAsync(cacheKey, value, storageOptions, cancellationToken).ConfigureAwait(false);
                await _distributed.SetAsync(metadataKey, metadata, storageOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cachify L2 set failed for {Key}", cacheKey);
                if (_options.FailFastOnL2Errors)
                {
                    throw;
                }
            }
        }

        if (_memory is not null)
        {
            await _memory.SetAsync(cacheKey, value, storageOptions, cancellationToken).ConfigureAwait(false);
            await _memory.SetAsync(metadataKey, metadata, storageOptions, cancellationToken).ConfigureAwait(false);
        }

        await PublishInvalidationAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        CacheSetTotal.Add(1);
    }

    /// <summary>
    /// Publishes an invalidation event to the configured backplane.
    /// </summary>
    /// <remarks>
    /// Design Notes: publishing is best-effort and should never block primary cache operations.
    /// </remarks>
    private async Task PublishInvalidationAsync(string cacheKey, CancellationToken cancellationToken)
    {
        if (_backplane is null || !_backplaneOptions.Enabled)
        {
            return;
        }

        try
        {
            var invalidation = CacheInvalidation.ForKey(cacheKey, _backplaneOptions.InstanceId);
            await _backplane.PublishInvalidationAsync(invalidation, cancellationToken).ConfigureAwait(false);
            BackplaneInvalidationPublishedTotal.Add(1, new KeyValuePair<string, object?>("type", "key"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cachify backplane publish failed for {Key}", cacheKey);
        }
    }

    /// <summary>
    /// Handles invalidation messages received from the backplane.
    /// </summary>
    /// <remarks>
    /// Design Notes: only L1 entries are cleared; L2 remains the source of truth for refills.
    /// </remarks>
    private async Task HandleBackplaneInvalidationAsync(CacheInvalidation invalidation, CancellationToken cancellationToken)
    {
        if (_memory is null || !_backplaneOptions.Enabled)
        {
            return;
        }

        if (string.Equals(invalidation.SourceId, _backplaneOptions.InstanceId, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(invalidation.Key))
        {
            await _memory.RemoveAsync(invalidation.Key, cancellationToken).ConfigureAwait(false);
            await _memory.RemoveAsync(BuildMetadataKey(invalidation.Key), cancellationToken).ConfigureAwait(false);
            BackplaneInvalidationReceivedTotal.Add(1, new KeyValuePair<string, object?>("type", "key"));
            return;
        }

        if (!string.IsNullOrWhiteSpace(invalidation.Tag))
        {
            _logger.LogDebug("Cachify backplane tag invalidation ignored for {Tag}", invalidation.Tag);
            BackplaneInvalidationReceivedTotal.Add(1, new KeyValuePair<string, object?>("type", "tag"));
        }
    }

    /// <summary>
    /// Resolves per-entry options with global defaults and TTL jitter.
    /// </summary>
    private CacheEntryOptions ResolveEntryOptions(CacheEntryOptions? options)
    {
        var resolved = options is null
            ? new CacheEntryOptions()
            : new CacheEntryOptions
            {
                TimeToLive = options.TimeToLive,
                SlidingExpiration = options.SlidingExpiration,
                JitterRatio = options.JitterRatio,
                NegativeCacheTtl = options.NegativeCacheTtl,
                KeyPrefix = options.KeyPrefix,
                SerializerName = options.SerializerName,
                Resilience = options.Resilience
            };

        var ttl = resolved.TimeToLive ?? _options.DefaultTtl;
        var jitterRatio = resolved.JitterRatio ?? _options.JitterRatio;
        resolved.TimeToLive = ApplyJitter(ttl, jitterRatio);
        return resolved;
    }

    /// <summary>
    /// Resolves resiliency options using per-entry overrides when provided.
    /// </summary>
    private CacheResilienceOptions ResolveResilienceOptions(CacheEntryOptions? options)
    {
        var defaultOptions = _options.Resilience;
        if (options?.Resilience is null)
        {
            return new CacheResilienceOptions
            {
                FailSafeMaxDuration = defaultOptions.FailSafeMaxDuration,
                SoftTimeout = defaultOptions.SoftTimeout,
                HardTimeout = defaultOptions.HardTimeout,
                EnableBackgroundRefresh = defaultOptions.EnableBackgroundRefresh
            };
        }

        var overrides = options.Resilience;
        return new CacheResilienceOptions
        {
            FailSafeMaxDuration = overrides.FailSafeMaxDuration,
            SoftTimeout = overrides.SoftTimeout ?? defaultOptions.SoftTimeout,
            HardTimeout = overrides.HardTimeout ?? defaultOptions.HardTimeout,
            EnableBackgroundRefresh = overrides.EnableBackgroundRefresh
        };
    }

    /// <summary>
    /// Builds metadata describing logical expiration for a cache entry.
    /// </summary>
    /// <remarks>
    /// Design Notes: logical expiration is decoupled from storage TTL to enable stale fallback
    /// while keeping storage simple and compatible with existing cache services.
    /// </remarks>
    private CacheEntryMetadata BuildMetadata(CacheEntryOptions options, CacheResilienceOptions resilience)
    {
        var now = _timeProvider.GetUtcNow();
        var ttl = options.TimeToLive ?? _options.DefaultTtl;
        var failSafe = resilience.FailSafeMaxDuration < TimeSpan.Zero ? TimeSpan.Zero : resilience.FailSafeMaxDuration;

        var logicalExpiration = now + ttl;
        var failSafeUntil = logicalExpiration + failSafe;

        return new CacheEntryMetadata
        {
            CreatedAtUtc = now,
            LogicalExpirationUtc = logicalExpiration,
            FailSafeUntilUtc = failSafeUntil
        };
    }

    /// <summary>
    /// Builds storage options that extend the TTL by the fail-safe window when enabled.
    /// </summary>
    /// <remarks>
    /// Design Notes: extending storage TTL preserves the payload long enough to serve stale values.
    /// </remarks>
    private CacheEntryOptions BuildStorageOptions(CacheEntryOptions options, CacheResilienceOptions resilience)
    {
        var storageOptions = new CacheEntryOptions
        {
            TimeToLive = options.TimeToLive,
            SlidingExpiration = options.SlidingExpiration,
            JitterRatio = options.JitterRatio,
            NegativeCacheTtl = options.NegativeCacheTtl,
            KeyPrefix = options.KeyPrefix,
            SerializerName = options.SerializerName,
            Resilience = options.Resilience
        };

        if (storageOptions.TimeToLive is not null && resilience.FailSafeMaxDuration > TimeSpan.Zero)
        {
            storageOptions.TimeToLive = storageOptions.TimeToLive.Value + resilience.FailSafeMaxDuration;
        }

        return storageOptions;
    }

    /// <summary>
    /// Applies jitter to a TTL to reduce synchronized expirations across keys.
    /// </summary>
    private TimeSpan ApplyJitter(TimeSpan ttl, double jitterRatio)
    {
        if (jitterRatio <= 0)
        {
            return ttl;
        }

        var jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRatio;
        var jittered = ttl.TotalMilliseconds * (1 + jitter);
        return TimeSpan.FromMilliseconds(Math.Max(1, jittered));
    }

    /// <summary>
    /// Reads a value and evaluates freshness using its metadata.
    /// </summary>
    /// <remarks>
    /// Design Notes: when metadata is missing, entries are treated as fresh to preserve backwards
    /// compatibility with values written before resiliency metadata existed.
    /// </remarks>
    private async Task<CacheEntryReadResult<T>> TryReadEntryAsync<T>(
        ICacheService cache,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var value = await cache.GetAsync<T>(cacheKey, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return new CacheEntryReadResult<T>(default, CacheEntryState.Miss, CacheEntryStaleReason.None);
        }

        var metadata = await cache.GetAsync<CacheEntryMetadata>(BuildMetadataKey(cacheKey), cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return new CacheEntryReadResult<T>(value, CacheEntryState.Fresh, CacheEntryStaleReason.None);
        }

        var now = _timeProvider.GetUtcNow();
        if (now <= metadata.LogicalExpirationUtc)
        {
            return new CacheEntryReadResult<T>(value, CacheEntryState.Fresh, CacheEntryStaleReason.None);
        }

        if (now <= metadata.FailSafeUntilUtc)
        {
            return new CacheEntryReadResult<T>(value, CacheEntryState.Stale, CacheEntryStaleReason.Expired);
        }

        return new CacheEntryReadResult<T>(default, CacheEntryState.Miss, CacheEntryStaleReason.None);
    }

    /// <summary>
    /// Copies metadata from a source cache to a destination cache layer and returns the TTL options for the value.
    /// </summary>
    /// <remarks>
    /// Design Notes: the remaining fail-safe window is used to align the L1 TTL with L2 metadata.
    /// </remarks>
    private async Task<CacheEntryOptions?> CopyMetadataAsync(
        ICacheService source,
        ICacheService destination,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var metadataKey = BuildMetadataKey(cacheKey);
        var metadata = await source.GetAsync<CacheEntryMetadata>(metadataKey, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return null;
        }

        var options = BuildOptionsFromMetadata(metadata);
        if (options is null)
        {
            return null;
        }

        await destination.SetAsync(metadataKey, metadata, options, cancellationToken).ConfigureAwait(false);
        return options;
    }

    /// <summary>
    /// Builds cache entry options from metadata for remaining TTL calculations.
    /// </summary>
    private CacheEntryOptions? BuildOptionsFromMetadata(CacheEntryMetadata metadata)
    {
        var remaining = metadata.FailSafeUntilUtc - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        return new CacheEntryOptions
        {
            TimeToLive = remaining
        };
    }

    /// <summary>
    /// Creates or reuses an in-flight refresh task for a key to avoid stampedes.
    /// </summary>
    /// <remarks>
    /// Design Notes: a shared refresh task allows soft-timeout callers to return stale data without
    /// duplicating factory work.
    /// </remarks>
    private Task<T> GetOrAddRefreshTask<T>(
        string cacheKey,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions resolvedOptions,
        CacheResilienceOptions resilience,
        bool allowBackground,
        CancellationToken cancellationToken)
    {
        var guardToken = allowBackground ? CancellationToken.None : cancellationToken;
        var task = _refreshTasks.GetOrAdd(cacheKey, _ =>
        {
            var refreshTask = _guard.RunAsync(cacheKey, async () =>
            {
                var secondCheck = await GetInternalAsync<T>(cacheKey, guardToken).ConfigureAwait(false);
                if (secondCheck.IsFresh)
                {
                    return secondCheck.Value!;
                }

                using var timeoutScope = CreateHardTimeoutScope(resilience, allowBackground ? CancellationToken.None : cancellationToken);
                var factoryTask = factory(timeoutScope.Token);
                if (timeoutScope.TimeoutTask is not null)
                {
                    var completed = await Task.WhenAny(factoryTask, timeoutScope.TimeoutTask).ConfigureAwait(false);
                    if (completed != factoryTask)
                    {
                        throw new TimeoutException("The cache factory exceeded the hard timeout.");
                    }
                }

                var value = await factoryTask.ConfigureAwait(false);
                await SetEntryAsync(cacheKey, value, resolvedOptions, resilience, guardToken).ConfigureAwait(false);
                return value;
            }, guardToken);

            refreshTask.ContinueWith(_ =>
            {
                _refreshTasks.TryRemove(cacheKey, out _);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            return refreshTask;
        });

        return (Task<T>)task;
    }

    /// <summary>
    /// Attempts to await a task up to a soft timeout, returning whether a timeout occurred.
    /// </summary>
    private async Task<(bool TimedOut, T? Result)> TryAwaitWithSoftTimeoutAsync<T>(
        Task<T> task,
        TimeSpan softTimeout,
        CancellationToken cancellationToken)
    {
        if (softTimeout <= TimeSpan.Zero)
        {
            return (false, await task.ConfigureAwait(false));
        }

        using var delayCts = _timeProvider.CreateCancellationTokenSource(softTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(delayCts.Token, cancellationToken);
        var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);

        var completed = await Task.WhenAny(task, delayTask).ConfigureAwait(false);
        if (completed == task)
        {
            return (false, await task.ConfigureAwait(false));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return (true, default);
    }

    /// <summary>
    /// Creates a scope that enforces the hard timeout for factory execution.
    /// </summary>
    private HardTimeoutScope CreateHardTimeoutScope(CacheResilienceOptions resilience, CancellationToken cancellationToken)
    {
        if (resilience.HardTimeout is null || resilience.HardTimeout.Value <= TimeSpan.Zero)
        {
            return new HardTimeoutScope(cancellationToken, null, null, null);
        }

        var timeoutCts = _timeProvider.CreateCancellationTokenSource(resilience.HardTimeout.Value);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);
        return new HardTimeoutScope(linkedCts.Token, timeoutCts, linkedCts, timeoutTask);
    }

    /// <summary>
    /// Schedules a background refresh when enabled, respecting cancellation.
    /// </summary>
    private void ScheduleRefresh<T>(
        string cacheKey,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions resolvedOptions,
        CacheResilienceOptions resilience,
        CancellationToken cancellationToken)
    {
        if (!resilience.EnableBackgroundRefresh || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _ = GetOrAddRefreshTask(cacheKey, factory, resolvedOptions, resilience, allowBackground: true, cancellationToken);
    }

    /// <summary>
    /// Records stale usage in activity tags and counters.
    /// </summary>
    private void RecordStale(Activity? activity, CacheEntryStaleReason reason, bool refreshScheduled)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("cachify.stale", true);
        if (reason != CacheEntryStaleReason.None)
        {
            activity.SetTag("cachify.stale_reason", reason.ToString());
        }

        if (reason == CacheEntryStaleReason.SoftTimeout)
        {
            activity.SetTag("cachify.timeout_type", "soft");
        }
        else if (reason == CacheEntryStaleReason.HardTimeout)
        {
            activity.SetTag("cachify.timeout_type", "hard");
        }

        activity.SetTag("cachify.refresh_scheduled", refreshScheduled);
        StaleServedTotal.Add(1);
        if (reason != CacheEntryStaleReason.None)
        {
            FailSafeUsedTotal.Add(1);
        }
    }

    /// <summary>
    /// Builds the cache key using configured prefixes.
    /// </summary>
    private string BuildKey(string key, CacheEntryOptions? options)
    {
        var prefix = options?.KeyPrefix ?? _options.KeyPrefix;
        return _keyBuilder.Build(key, null, prefix);
    }

    /// <summary>
    /// Builds the metadata key for a cached value.
    /// </summary>
    /// <remarks>
    /// Design Notes: a suffix is used to avoid interfering with existing key formats and to keep
    /// metadata separate from user payloads.
    /// </remarks>
    private string BuildMetadataKey(string cacheKey)
    {
        return string.Concat(cacheKey, MetadataSuffix);
    }

    /// <summary>
    /// Records a cache hit with the specified layer tag.
    /// </summary>
    private void RecordHit(string layer)
    {
        CacheHitTotal.Add(1, new KeyValuePair<string, object?>("layer", layer));
    }

    /// <summary>
    /// Records a cache miss.
    /// </summary>
    private void RecordMiss()
    {
        CacheMissTotal.Add(1);
    }

    /// <summary>
    /// Represents a hard-timeout scope for factory execution.
    /// </summary>
    /// <remarks>
    /// Design Notes: encapsulates timeout CTS instances to ensure timely disposal and avoid
    /// timer leaks even when factories throw or are canceled.
    /// </remarks>
    private readonly struct HardTimeoutScope : IDisposable
    {
        public HardTimeoutScope(
            CancellationToken token,
            CancellationTokenSource? timeoutCts,
            CancellationTokenSource? linkedCts,
            Task? timeoutTask)
        {
            Token = token;
            TimeoutCts = timeoutCts;
            LinkedCts = linkedCts;
            TimeoutTask = timeoutTask;
        }

        public CancellationToken Token { get; }

        public CancellationTokenSource? TimeoutCts { get; }

        public CancellationTokenSource? LinkedCts { get; }

        public Task? TimeoutTask { get; }

        public void Dispose()
        {
            LinkedCts?.Dispose();
            TimeoutCts?.Dispose();
        }
    }

    /// <summary>
    /// Releases resources used by the composite cache service.
    /// </summary>
    public void Dispose()
    {
        _backplaneSubscription?.Dispose();
    }
}
