# Cachify

<img width="512" height="512" alt="Image" src="https://github.com/user-attachments/assets/8e5d4f11-0195-4b1e-8ac7-2f1b301dd2a3" />

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
- `Cachify.Redis` — Redis provider (L2) + Redis backplane (optional).
- `Cachify.AspNetCore` — DI + options integration.

## Request/response caching (ASP.NET Core)

Cachify ships a request caching layer that uses the core cache stack underneath. You can integrate it through
middleware, endpoint metadata, or by injecting the `RequestCacheService` directly.

### Registering services

```csharp
builder.Services.AddCachify(options =>
{
    options.KeyPrefix = "myapp";
    options.DefaultTtl = TimeSpan.FromMinutes(5);
    options.UseMemory();
});

builder.Services.AddRequestCaching(options =>
{
    options.DefaultDuration = TimeSpan.FromSeconds(60);
    options.CacheableMethods.Add(HttpMethods.Post); // opt in to POST caching if desired
    options.KeyOptions.IncludeBody = true;
});
```

### Middleware usage

```csharp
app.UseRouting();
app.UseRequestCaching();
app.MapGet("/weather", () => Results.Json(new { Temperature = 72 }));
```

### Endpoint metadata (attribute + minimal APIs)

```csharp
[RequestCache(DurationSeconds = 30, IncludeRequestBody = false)]
public IActionResult GetWeather() => Ok(new { Temperature = 72 });

app.MapPost("/echo", (string value) => Results.Text(value))
   .WithRequestCaching(policy =>
   {
       policy.Duration = TimeSpan.FromSeconds(15);
       policy.IncludeRequestBody = true;
       policy.CacheableMethods = new[] { HttpMethods.Post };
   });
```

### Direct API usage

```csharp
public async Task<IResult> GetAsync(HttpContext context, RequestCacheService cacheService)
{
    await cacheService.ExecuteAsync(context, null, async ct =>
    {
        var payload = new { Message = "hello" };
        await context.Response.WriteAsJsonAsync(payload, ct);
    });

    return Results.Empty;
}
```

### Metadata headers

By default, request caching emits response headers to indicate cache hits/misses and stale responses:

- `X-Cachify-Cache`: `HIT` or `MISS`
- `X-Cachify-Cache-Stale`: `true` or `false`
- `X-Cachify-Cache-Similarity`: similarity score when similarity caching is used

Use `RequestCacheMetadataAccessor.TryGetMetadata` to access the same information programmatically when needed.

### Safety defaults

- Requests with `Authorization` headers are not cached unless `CacheAuthenticatedResponses` is enabled.
- `Cache-Control: no-store`, `no-cache`, or `private` prevents caching by default.
- `Set-Cookie` responses are excluded by default unless explicitly enabled.

## Similarity request caching (LLM MVP)

Similarity request caching allows near-duplicate LLM calls to reuse cached responses without storing full payloads.
It is optional and disabled by default; core caching users do not pay the cost unless `Mode` is set to `Similarity`.

### How it works

1. **Canonicalization**: request payloads are normalized (stable JSON ordering + noise field removal).
2. **Hashing**: a stable SHA-256 hash is computed for exact cache storage.
3. **Signature**: a compact 64-bit SimHash signature is generated for similarity scoring.
4. **Indexing**: signatures are placed into LSH-style buckets (four 16-bit bands) to shortlist candidates.
5. **Scoring**: candidates are scored using a pluggable similarity scorer (default SimHash Hamming similarity).

### Trade-offs

- Similarity caching trades strict correctness for speed and reuse of near-duplicate prompts.
- SimHash is cheap and compact, but may miss semantically similar prompts without explicit overlap.
- Embedding-based scorers can improve quality but increase memory usage; they are opt-in.

### Privacy and security notes

- Payloads are not stored raw in the index; only compact signatures, hash prefixes, and cache keys are retained.
- Size limits (`MaxRequestBodySizeBytes`, `MaxCanonicalLength`) prevent large payload retention.
- Configure `IgnoredJsonFields` to remove sensitive or noisy fields from canonicalization.

### Recommended defaults

- `MinSimilarity`: `0.95`
- `MaxEntryAge`: `10 minutes`
- `MaxIndexEntries`: `1024`
- `MaxCandidates`: `64`

### Example: LLM request payload similarity

```csharp
builder.Services.AddRequestCaching(options =>
{
    options.Mode = RequestCacheMode.Similarity;
    options.CacheableMethods.Add(HttpMethods.Post);
    options.Similarity.Enabled = true;
    options.Similarity.MinSimilarity = 0.95;
});

app.MapPost("/llm", async (HttpContext context) =>
{
    // Simulated LLM response
    await context.Response.WriteAsync($"response:{DateTimeOffset.UtcNow:O}");
});
```

```json
// First request (cached)
{"prompt":"Summarize the release notes","id":"abc123"}

// Second request (served from cache, id ignored)
{"prompt":"Summarize the release notes","id":"def456"}
```

The second request will be served from cache when the similarity score meets the threshold. The response will
include `X-Cachify-Cache-Similarity` to expose the score used for the decision.

## Configuration

Key options on `CachifyOptions`:

- `KeyPrefix`
- `DefaultTtl`
- `JitterRatio`
- `Backplane` (optional distributed invalidation)

## Observability

Cachify emits metrics and traces:

- Meter name: `Cachify`
- Counters: `cache_hit_total`, `cache_miss_total`, `cache_set_total`, `cache_remove_total`
- Counters: `cache_backplane_invalidation_published_total`, `cache_backplane_invalidation_received_total`
- Counters: `similarity_cache_hit`, `similarity_cache_miss`, `similarity_candidates_count`
- Counters: `stale_served_count`, `factory_timeout_soft_count`, `factory_timeout_hard_count`, `failsafe_used_count`
- Histogram: `cache_get_duration_ms`
- Histogram: `similarity_best_score_histogram`
- Activity source: `Cachify`

## Backplane invalidation (optional)

Enable distributed L1 invalidation using a backplane (Redis pub/sub):

```csharp
builder.Services.AddCachify(options =>
{
    options.KeyPrefix = "myapp";
    options.Backplane.Enabled = true;
    options.Backplane.ChannelName = "cachify:invalidation";
    options.Backplane.InstanceId = Environment.MachineName;

    options.UseMemory();
    options.UseRedis(redis =>
    {
        redis.ConnectionString = "localhost:6379";
    });
});

builder.Services.AddSingleton<ICacheBackplane, RedisBackplane>();
```

## Resiliency (MVP)

Cachify includes a lightweight resiliency layer in the composite orchestrator. It preserves a small public surface
by using a single `CacheResilienceOptions` object (global or per-entry) and internal metadata stored alongside entries.

### How it works

- **Fail-safe stale fallback**: entries are stored for `TTL + FailSafeMaxDuration`. Logical expiration is tracked in
  metadata, so stale values can be served when the factory fails or times out.
- **Soft timeout**: if a factory exceeds `SoftTimeout`, Cachify returns a stale value (if available) while the refresh
  continues in the background.
- **Hard timeout**: if a factory exceeds `HardTimeout`, the factory is canceled and a timeout is thrown unless a stale
  value is available.
- **Background refresh**: when stale is served due to fail-safe or timeouts, a refresh is scheduled with stampede
  protection to keep only one refresh per key in flight.

**Failure behavior**: when L2 fails and a stale value exists in L1, Cachify serves the stale entry and logs the L2
error instead of failing the call (unless `FailFastOnL2Errors` is enabled and no stale exists). Stale responses are
tagged in activities (`cachify.stale`, `cachify.stale_reason`, `cachify.timeout_type`) for observability.

## Roadmap

- Memcached provider
- Negative caching
- Advanced failure policies
