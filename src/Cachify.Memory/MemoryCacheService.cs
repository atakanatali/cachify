using Cachify.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace Cachify.Memory;

public sealed class MemoryCacheService : IMemoryCacheService, ISingleCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ICacheSerializer _serializer;

    public MemoryCacheService(IMemoryCache cache, ICacheSerializer serializer)
    {
        _cache = cache;
        _serializer = serializer;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var payload) && payload is byte[] bytes)
        {
            return Task.FromResult(_serializer.Deserialize<T>(bytes));
        }

        return Task.FromResult<T?>(default);
    }

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

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public async Task<T> GetOrSetAsync(
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
