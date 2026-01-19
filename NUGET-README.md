# Cachify

**A modular caching stack for .NET with layered L1/L2 support**

---

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

## Features

- **L1/L2 Layered Caching** - Memory + Redis with automatic population
- **Stampede Protection** - Single factory execution per key
- **Backplane Invalidation** - Distributed L1 cache sync via Redis Pub/Sub
- **Request Caching** - HTTP response caching middleware for ASP.NET Core
- **Similarity Caching** - Near-duplicate LLM request reuse (optional)
- **Resiliency** - Soft/hard timeouts, stale fallback, background refresh
- **Observability** - Metrics and tracing via OpenTelemetry

## Package Structure

| Package | Description |
|---------|-------------|
| `Cachify.Abstractions` | Interfaces and core models |
| `Cachify.Core` | Composite orchestration, stampede guard |
| `Cachify.Memory` | In-memory provider (L1) |
| `Cachify.Redis` | Redis provider (L2) + backplane |
| `Cachify.AspNetCore` | DI + request caching middleware |

## Documentation

For full documentation, visit [GitHub](https://github.com/atakanatali/Cachify).
