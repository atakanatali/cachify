namespace Cachify.AspNetCore;

/// <summary>
/// Captures metadata about a cached response for observability and diagnostics.
/// </summary>
public sealed record RequestCacheMetadata(
    bool IsHit,
    bool IsStale,
    double? SimilarityScore,
    string CacheKey,
    DateTimeOffset CachedAt,
    TimeSpan Duration);
