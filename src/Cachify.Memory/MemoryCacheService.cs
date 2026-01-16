using Cachify.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace Cachify.Memory;

/// <summary>
/// Implements an in-memory cache service backed by <see cref="IMemoryCache"/>.
/// </summary>
public sealed class MemoryCacheService : IMemoryCacheService, ISingleCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ICacheSerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryCacheService"/> class.
    /// </summary>
    /// <param name="cache">The underlying memory cache.</param>
    /// <param name="serializer">The serializer used for payloads.</param>
    public MemoryCacheService(IMemoryCache cache, ICacheSerializer serializer)
    {
        _cache = cache;
        _serializer = serializer;
    }

    /// <inheritdoc />
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var payload) && payload is byte[] bytes)
        {
            return Task.FromResult(_serializer.Deserialize<T>(bytes));
        }

        return Task.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var bytes = _serializer.Serialize(value);
        var entryOptions = new MemoryCacheEntryOptions();

        if (options?.TimeToLive is not null)
        {
            entryOptions.SetAbsoluteExpiration(options.TimeToLive.Value);
        }

        if (options?.SlidingExpiration is not null)
        {
            entryOptions.SetSlidingExpiration(options.SlidingExpiration.Value);
        }

        _cache.Set(key, bytes, entryOptions);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
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
