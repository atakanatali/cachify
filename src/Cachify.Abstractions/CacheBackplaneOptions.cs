using System;

namespace Cachify.Abstractions;

/// <summary>
/// Defines configuration for cache backplane invalidation.
/// </summary>
/// <remarks>
/// Design Notes: these options keep the backplane optional while still allowing consistent
/// instance identifiers, channel naming, and batching behavior across transports.
/// </remarks>
public sealed class CacheBackplaneOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether backplane invalidation is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the channel name used to broadcast invalidation messages.
    /// </summary>
    public string ChannelName { get; set; } = "cachify:invalidation";

    /// <summary>
    /// Gets or sets the unique instance identifier attached to invalidation messages.
    /// </summary>
    /// <remarks>
    /// Design Notes: this identifier is used to suppress handling of locally published events.
    /// </remarks>
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the maximum number of invalidations to batch in a single message.
    /// </summary>
    /// <remarks>
    /// Design Notes: values less than or equal to 1 disable batching and publish immediately.
    /// </remarks>
    public int BatchSize { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum time to wait before flushing a batch.
    /// </summary>
    /// <remarks>
    /// Design Notes: set to <see cref="TimeSpan.Zero"/> to disable time-based batching.
    /// </remarks>
    public TimeSpan BatchWindow { get; set; } = TimeSpan.Zero;
}
