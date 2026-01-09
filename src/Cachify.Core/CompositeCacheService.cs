using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cachify.Abstractions;
using Microsoft.Extensions.Logging;

namespace Cachify.Core;

public sealed class CompositeCacheService : ICompositeCacheService
{
    private static readonly Meter Meter = new("Cachify");
    private static readonly Counter<long> CacheHitTotal = Meter.CreateCounter<long>("cache_hit_total");
    private static readonly Counter<long> CacheMissTotal = Meter.CreateCounter<long>("cache_miss_total");
    private static readonly Counter<long> CacheSetTotal = Meter.CreateCounter<long>("cache_set_total");
    private static readonly Counter<long> CacheRemoveTotal = Meter.CreateCounter<long>("cache_remove_total");
    private static readonly Histogram<double> CacheGetDuration = Meter.CreateHistogram<double>("cache_get_duration_ms");

    private static readonly ActivitySource ActivitySource = new("Cachify");

    private readonly IMemoryCacheService? _memory;
    private readonly IDistributedCacheService? _distributed;
    private readonly CachifyOptions _options;
    private readonly ICacheKeyBuilder _keyBuilder;
    private readonly CacheStampedeGuard _guard;
    private readonly ILogger<CompositeCacheService> _logger;

    public CompositeCacheService(
        IMemoryCacheService? memory,
        IDistributedCacheService? distributed,
        CachifyOptions options,
        ICacheKeyBuilder keyBuilder,
        CacheStampedeGuard guard,
        ILogger<CompositeCacheService> logger)
    {
        _memory = memory;
        _distributed = distributed;
        _options = options;
        _keyBuilder = keyBuilder;
        _guard = guard;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildKey(key, null);
        return await GetInternalAsync<T>(cacheKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildKey(key, options);
        using var activity = ActivitySource.StartActivity("cachify.set", ActivityKind.Internal);

        var resolvedOptions = ResolveEntryOptions(options);
        await SetInternalAsync(cacheKey, value, resolvedOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildKey(key, null);
        using var activity = ActivitySource.StartActivity("cachify.remove", ActivityKind.Internal);

        if (_distributed is not null)
        {
            try
            {
                await _distributed.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
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
        }

        CacheRemoveTotal.Add(1);
    }

    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildKey(key, options);
        using var activity = ActivitySource.StartActivity("cachify.getorset", ActivityKind.Internal);
        var fromCache = await GetInternalAsync<T>(cacheKey, cancellationToken).ConfigureAwait(false);
        if (fromCache is not null)
        {
            return fromCache;
        }

        return await _guard.RunAsync(cacheKey, async () =>
        {
            var secondCheck = await GetInternalAsync<T>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (secondCheck is not null)
            {
                return secondCheck;
            }

            var value = await factory(cancellationToken).ConfigureAwait(false);
            var resolvedOptions = ResolveEntryOptions(options);
            await SetInternalAsync(cacheKey, value, resolvedOptions, cancellationToken).ConfigureAwait(false);
            return value;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> GetInternalAsync<T>(string cacheKey, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("cachify.get", ActivityKind.Internal);
        var start = Stopwatch.GetTimestamp();

        if (_memory is not null)
        {
            var value = await _memory.GetAsync<T>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (value is not null)
            {
                RecordHit("L1");
                CacheGetDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                return value;
            }
        }

        if (_distributed is not null)
        {
            try
            {
                var value = await _distributed.GetAsync<T>(cacheKey, cancellationToken).ConfigureAwait(false);
                if (value is not null)
                {
                    RecordHit("L2");
                    if (_memory is not null)
                    {
                        await _memory.SetAsync(cacheKey, value, null, cancellationToken).ConfigureAwait(false);
                    }

                    CacheGetDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    return value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cachify L2 get failed for {Key}", cacheKey);
                if (_options.FailFastOnL2Errors)
                {
                    throw;
                }
            }
        }

        RecordMiss();
        CacheGetDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return default;
    }

    private async Task SetInternalAsync<T>(string cacheKey, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
    {
        if (_distributed is not null)
        {
            try
            {
                await _distributed.SetAsync(cacheKey, value, options, cancellationToken).ConfigureAwait(false);
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
            await _memory.SetAsync(cacheKey, value, options, cancellationToken).ConfigureAwait(false);
        }

        CacheSetTotal.Add(1);
    }

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
                SerializerName = options.SerializerName
            };

        var ttl = resolved.TimeToLive ?? _options.DefaultTtl;
        var jitterRatio = resolved.JitterRatio ?? _options.JitterRatio;
        resolved.TimeToLive = ApplyJitter(ttl, jitterRatio);
        return resolved;
    }

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

    private string BuildKey(string key, CacheEntryOptions? options)
    {
        var prefix = options?.KeyPrefix ?? _options.KeyPrefix;
        return _keyBuilder.Build(key, null, prefix);
    }

    private void RecordHit(string layer)
    {
        CacheHitTotal.Add(1, new KeyValuePair<string, object?>("layer", layer));
    }

    private void RecordMiss()
    {
        CacheMissTotal.Add(1);
    }
}
