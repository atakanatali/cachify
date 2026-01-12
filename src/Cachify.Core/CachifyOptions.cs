using System;
using Cachify.Abstractions;

namespace Cachify.Core;

/// <summary>
/// Defines global options for Cachify.
/// </summary>
public sealed class CachifyOptions
{
    /// <summary>
    /// Gets or sets the global cache key prefix applied to entries.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Gets or sets the default time-to-live for cache entries.
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the jitter ratio applied to entry TTLs to reduce stampedes.
    /// </summary>
    public double JitterRatio { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets a value indicating whether L2 failures should be propagated to the caller.
    /// </summary>
    public bool FailFastOnL2Errors { get; set; }

    /// <summary>
    /// Gets or sets the resiliency defaults applied to cache entries.
    /// </summary>
    public CacheResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Gets or sets the time provider used for time-based operations.
    /// </summary>
    /// <remarks>
    /// Design Notes: exposing the time provider keeps timing tests deterministic without inflating the cache API surface.
    /// </remarks>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
