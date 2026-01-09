using Cachify.Core;
using Cachify.Memcached;
using Cachify.Redis;

namespace Cachify.AspNetCore;

public sealed class CachifyBuilderOptions : CachifyOptions
{
    internal bool MemoryEnabled { get; private set; }
    internal bool RedisEnabled { get; private set; }
    internal bool MemcachedEnabled { get; private set; }

    internal RedisOptions? RedisOptions { get; private set; }
    internal MemcachedOptions? MemcachedOptions { get; private set; }

    public void UseMemory()
    {
        MemoryEnabled = true;
    }

    public void UseRedis(Action<RedisOptions> configure)
    {
        RedisEnabled = true;
        var options = new RedisOptions();
        configure(options);
        RedisOptions = options;
    }

    public void UseMemcached(Action<MemcachedOptions> configure)
    {
        MemcachedEnabled = true;
        var options = new MemcachedOptions();
        configure(options);
        MemcachedOptions = options;
    }
}
