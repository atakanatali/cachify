using System;

namespace Cachify.Abstractions;

/// <summary>
/// Configures resiliency behaviors for cache entries such as fail-safe stale usage
/// and factory timeouts.
/// </summary>
public sealed class CacheResilienceOptions
{
    /// <summary>
    /// Gets or sets the maximum duration to serve stale values after the logical TTL has elapsed.
    /// A value of <see cref="TimeSpan.Zero"/> disables stale fallback.
    /// </summary>
    /// <remarks>
    /// Design Notes: the stored entry is kept alive for the TTL plus this window so that the value
    /// can still be retrieved when the factory fails or times out.
    /// </remarks>
    public TimeSpan FailSafeMaxDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets the soft timeout for the value factory. When elapsed, a stale value is returned
    /// (if available) and the factory is allowed to continue in the background.
    /// </summary>
    /// <remarks>
    /// Design Notes: soft timeouts reduce tail latency while preserving refresh in flight.
    /// </remarks>
    public TimeSpan? SoftTimeout { get; set; }

    /// <summary>
    /// Gets or sets the hard timeout for the value factory. When elapsed, the factory is canceled
    /// and a stale value is returned if available; otherwise a timeout is thrown.
    /// </summary>
    /// <remarks>
    /// Design Notes: hard timeouts protect the system from runaway work and resource exhaustion.
    /// </remarks>
    public TimeSpan? HardTimeout { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether background refresh should be scheduled when a stale
    /// value is served.
    /// </summary>
    /// <remarks>
    /// Design Notes: enabled by default to keep cache warm after fail-safe responses.
    /// </remarks>
    public bool EnableBackgroundRefresh { get; set; } = true;
}
