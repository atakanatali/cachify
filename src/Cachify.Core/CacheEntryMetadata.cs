using System;

namespace Cachify.Core;

/// <summary>
/// Captures logical expiration metadata for a cache entry stored alongside the value.
/// </summary>
/// <remarks>
/// Design Notes: the value itself is kept alive for TTL + fail-safe window, while this metadata
/// records the logical expiration boundary and the fail-safe window used to decide staleness.
/// </remarks>
internal sealed class CacheEntryMetadata
{
    /// <summary>
    /// Gets or sets the UTC time when the entry was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time when the entry becomes logically expired.
    /// </summary>
    public DateTimeOffset LogicalExpirationUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time until which stale values are considered eligible.
    /// </summary>
    public DateTimeOffset FailSafeUntilUtc { get; set; }
}
