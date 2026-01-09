# Cachify

<img width="256" height="256" alt="Image" src="https://github.com/user-attachments/assets/3b8a48ed-b313-4a01-b6a5-0514a942fd6a" />

Cachify is a modular caching stack for .NET that supports layered L1/L2 caching with a minimal API and strong defaults.

## Quickstart

```csharp
builder.Services.AddCachify(options =>
{
    options.KeyPrefix = "myapp";
    options.DefaultTtl = TimeSpan.FromMinutes(5);
    options.JitterRatio = 0.1;

    options.UseMemory();
    options.UseRedis(redis =>
    {
        redis.ConnectionString = "localhost:6379";
    });
});
```

```csharp
var value = await cache.GetOrSetAsync(
    "user:42",
    async ct => await LoadUserAsync(ct),
    new CacheEntryOptions { TimeToLive = TimeSpan.FromMinutes(2) },
    cancellationToken);
```

## Packages

- `Cachify.Abstractions` — interfaces and core models.
- `Cachify.Core` — composite orchestration, serializers, key builder, stampede guard.
- `Cachify.Memory` — in-memory provider (L1).
- `Cachify.Redis` — Redis provider (L2).
- `Cachify.AspNetCore` — DI + options integration.

## Configuration

Key options on `CachifyOptions`:

- `KeyPrefix`
- `DefaultTtl`
- `JitterRatio`

## Observability

Cachify emits metrics and traces:

- Meter name: `Cachify`
- Counters: `cache_hit_total`, `cache_miss_total`, `cache_set_total`, `cache_remove_total`
- Histogram: `cache_get_duration_ms`
- Activity source: `Cachify`

## Roadmap

- Memcached provider
- Negative caching
- Advanced failure policies
