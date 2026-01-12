using Cachify.Core;
using Cachify.Memcached;
using Cachify.Redis;

namespace Cachify.AspNetCore;

/// <summary>
/// Configures Cachify services via dependency injection.
/// </summary>
public sealed class CachifyBuilderOptions : CachifyOptions
{
    internal bool MemoryEnabled { get; private set; }
    internal bool RedisEnabled { get; private set; }
    internal bool MemcachedEnabled { get; private set; }

    internal RedisOptions? RedisOptions { get; private set; }
    internal MemcachedOptions? MemcachedOptions { get; private set; }

    /// <summary>
    /// Enables the in-memory cache provider.
    /// </summary>
    public void UseMemory()
    {
        MemoryEnabled = true;
    }

    /// <summary>
    /// Enables the Redis cache provider.
    /// </summary>
    /// <param name="configure">The Redis configuration action.</param>
    public void UseRedis(Action<RedisOptions> configure)
    {
        RedisEnabled = true;
        var options = new RedisOptions();
        configure(options);
        RedisOptions = options;
    }

    /// <summary>
    /// Enables the Memcached cache provider.
    /// </summary>
    /// <param name="configure">The Memcached configuration action.</param>
    public void UseMemcached(Action<MemcachedOptions> configure)
    {
        MemcachedEnabled = true;
        var options = new MemcachedOptions();
        configure(options);
        MemcachedOptions = options;
    }
}
