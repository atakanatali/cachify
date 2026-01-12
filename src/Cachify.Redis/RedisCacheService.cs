using Cachify.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cachify.Redis;

/// <summary>
/// Implements a Redis-backed cache service.
/// </summary>
public sealed class RedisCacheService : IDistributedCacheService, ISingleCacheService
{
    private readonly IConnectionMultiplexer _connection;
    private readonly ICacheSerializer _serializer;
    private readonly ILogger<RedisCacheService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisCacheService"/> class.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer.</param>
    /// <param name="serializer">The serializer used for payloads.</param>
    /// <param name="logger">The logger instance.</param>
    public RedisCacheService(
        IConnectionMultiplexer connection,
        ICacheSerializer serializer,
        ILogger<RedisCacheService> logger)
    {
        _connection = connection;
        _serializer = serializer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var value = await db.StringGetAsync(key).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
        {
            return default;
        }

        return _serializer.Deserialize<T>(value!);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var payload = _serializer.Serialize(value);
        var ttl = options?.TimeToLive;

        try
        {
            await db.StringSetAsync(key, payload, ttl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis set failed for {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        try
        {
            await db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis remove failed for {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
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
