namespace Cachify.AspNetCore;

/// <summary>
/// Configures metadata headers emitted by request caching.
/// </summary>
public sealed class RequestCacheHeaderOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether cache metadata headers are emitted.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the header name used to describe cache hits and misses.
    /// </summary>
    public string CacheStatusHeader { get; set; } = "X-Cachify-Cache";

    /// <summary>
    /// Gets or sets the header name used to describe stale responses.
    /// </summary>
    public string CacheStaleHeader { get; set; } = "X-Cachify-Cache-Stale";

    /// <summary>
    /// Gets or sets the header name used to expose a similarity score when available.
    /// </summary>
    public string SimilarityHeader { get; set; } = "X-Cachify-Cache-Similarity";

    /// <summary>
    /// Gets or sets the header name used to expose the cache key when enabled.
    /// </summary>
    public string CacheKeyHeader { get; set; } = "X-Cachify-Cache-Key";

    /// <summary>
    /// Gets or sets a value indicating whether the cache key is emitted as a response header.
    /// </summary>
    public bool IncludeCacheKey { get; set; }
}
