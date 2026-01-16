using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Cachify.Abstractions;

namespace Cachify.Tests;

/// <summary>
/// A deterministic in-memory cache service for unit testing.
/// </summary>
internal sealed class TestCacheService : IMemoryCacheService, IDistributedCacheService, ISingleCacheService
{
    private readonly ConcurrentDictionary<string, object?> _entries = new();
    private int _setCount;

    /// <summary>
    /// Gets the number of successful set operations.
    /// </summary>
    public int SetCount => _setCount;

    /// <summary>
    /// Gets or sets a value indicating whether get operations should throw.
    /// </summary>
    public bool ThrowOnGet { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether set operations should throw.
    /// </summary>
    public bool ThrowOnSet { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether remove operations should throw.
    /// </summary>
    public bool ThrowOnRemove { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked after a successful set operation.
    /// </summary>
    public Action<string, object?>? OnSet { get; set; }

    /// <inheritdoc />
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (ThrowOnGet)
        {
            throw new InvalidOperationException("Get failed.");
        }

        if (_entries.TryGetValue(key, out var value) && value is T typed)
        {
            return Task.FromResult<T?>(typed);
        }

        return Task.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSet)
        {
            throw new InvalidOperationException("Set failed.");
        }

        _entries[key] = value;
        Interlocked.Increment(ref _setCount);
        OnSet?.Invoke(key, value);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (ThrowOnRemove)
        {
            throw new InvalidOperationException("Remove failed.");
        }

        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, value, options, cancellationToken).ConfigureAwait(false);
        return value;
    }
}
