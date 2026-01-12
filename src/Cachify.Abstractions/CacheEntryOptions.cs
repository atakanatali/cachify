using System;

namespace Cachify.Abstractions;

/// <summary>
/// Defines per-entry caching options such as TTL, expiration behavior, and resiliency overrides.
/// </summary>
public sealed class CacheEntryOptions
{
    /// <summary>
    /// Gets or sets the absolute time-to-live for the entry.
    /// </summary>
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the sliding expiration window for the entry.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>
    /// Gets or sets the jitter ratio applied to the TTL to mitigate stampedes.
    /// </summary>
    public double? JitterRatio { get; set; }

    /// <summary>
    /// Gets or sets the negative cache TTL, if negative caching is enabled by the caller.
    /// </summary>
    public TimeSpan? NegativeCacheTtl { get; set; }

    /// <summary>
    /// Gets or sets the optional key prefix override for this entry.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Gets or sets the serializer name override for this entry.
    /// </summary>
    public string? SerializerName { get; set; }

    /// <summary>
    /// Gets or sets per-entry resiliency overrides.
    /// </summary>
    public CacheResilienceOptions? Resilience { get; set; }
}
