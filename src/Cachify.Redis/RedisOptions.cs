using System;

namespace Cachify.Redis;

/// <summary>
/// Configures Redis connectivity for Cachify.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the connection timeout in milliseconds.
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the sync timeout in milliseconds.
    /// </summary>
    public int SyncTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the optional key prefix for Redis entries.
    /// </summary>
    public string? KeyPrefix { get; set; }
}
