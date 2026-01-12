using Cachify.Abstractions;
using Cachify.Core;
using Cachify.Memory;
using Cachify.Redis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cachify.AspNetCore;

/// <summary>
/// Provides dependency injection extensions for Cachify.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Cachify services using the provided builder configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The builder configuration action.</param>
    public static IServiceCollection AddCachify(
        this IServiceCollection services,
        Action<CachifyBuilderOptions> configure)
    {
        var builderOptions = new CachifyBuilderOptions();
        configure(builderOptions);

        services.AddSingleton<CachifyOptions>(builderOptions);
        services.AddSingleton<ICacheSerializer, JsonCacheSerializer>();
        services.AddSingleton<ICacheKeyBuilder, DefaultCacheKeyBuilder>();
        services.AddSingleton<CacheStampedeGuard>();

        if (builderOptions.MemoryEnabled)
        {
            services.AddMemoryCache();
            services.AddSingleton<IMemoryCacheService, MemoryCacheService>();
        }

        if (builderOptions.RedisEnabled && builderOptions.RedisOptions is not null)
        {
            services.AddSingleton(builderOptions.RedisOptions);
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(builderOptions.RedisOptions.ConnectionString);
                options.ConnectTimeout = builderOptions.RedisOptions.ConnectTimeoutMs;
                options.SyncTimeout = builderOptions.RedisOptions.SyncTimeoutMs;
                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<IDistributedCacheService, RedisCacheService>();
        }

        services.AddSingleton<ICompositeCacheService>(sp =>
        {
            var memory = sp.GetService<IMemoryCacheService>();
            var distributed = sp.GetService<IDistributedCacheService>();
            var options = sp.GetRequiredService<CachifyOptions>();
            var keyBuilder = sp.GetRequiredService<ICacheKeyBuilder>();
            var guard = sp.GetRequiredService<CacheStampedeGuard>();
            var logger = sp.GetRequiredService<ILogger<CompositeCacheService>>();
            return new CompositeCacheService(memory, distributed, options, keyBuilder, guard, logger);
        });

        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<ICompositeCacheService>());

        return services;
    }
}
