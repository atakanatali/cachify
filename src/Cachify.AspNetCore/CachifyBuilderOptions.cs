using Cachify.Abstractions;
using Cachify.Core;
using Cachify.Memcached;
using Cachify.Redis;

namespace Cachify.AspNetCore;

/// <summary>
/// Configures Cachify services via dependency injection.
/// </summary>
public sealed class CachifyBuilderOptions
{
    internal CachifyOptions CoreOptions { get; } = new();

    internal bool MemoryEnabled { get; private set; }
    internal bool RedisEnabled { get; private set; }
    internal bool MemcachedEnabled { get; private set; }

    internal RedisOptions? RedisOptions { get; private set; }
    internal MemcachedOptions? MemcachedOptions { get; private set; }

    /// <summary>
    /// Gets or sets the global cache key prefix applied to entries.
    /// </summary>
    public string? KeyPrefix
    {
        get => CoreOptions.KeyPrefix;
        set => CoreOptions.KeyPrefix = value;
    }

    /// <summary>
    /// Gets or sets the default time-to-live for cache entries.
    /// </summary>
    public TimeSpan DefaultTtl
    {
        get => CoreOptions.DefaultTtl;
        set => CoreOptions.DefaultTtl = value;
    }

    /// <summary>
    /// Gets or sets the jitter ratio applied to entry TTLs to reduce stampedes.
    /// </summary>
    public double JitterRatio
    {
        get => CoreOptions.JitterRatio;
        set => CoreOptions.JitterRatio = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether L2 failures should be propagated to the caller.
    /// </summary>
    public bool FailFastOnL2Errors
    {
        get => CoreOptions.FailFastOnL2Errors;
        set => CoreOptions.FailFastOnL2Errors = value;
    }

    /// <summary>
    /// Gets or sets the resiliency defaults applied to cache entries.
    /// </summary>
    public CacheResilienceOptions Resilience
    {
        get => CoreOptions.Resilience;
        set => CoreOptions.Resilience = value;
    }

    /// <summary>
    /// Gets or sets the time provider used for time-based operations.
    /// </summary>
    public TimeProvider TimeProvider
    {
        get => CoreOptions.TimeProvider;
        set => CoreOptions.TimeProvider = value;
    }

    /// <summary>
    /// Gets or sets the backplane configuration for distributed invalidation.
    /// </summary>
    public CacheBackplaneOptions Backplane
    {
        get => CoreOptions.Backplane;
        set => CoreOptions.Backplane = value;
    }

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
