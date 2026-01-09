using System;

namespace Cachify.Redis;

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";

    public int ConnectTimeoutMs { get; set; } = 5000;

    public int SyncTimeoutMs { get; set; } = 5000;

    public string? KeyPrefix { get; set; }
}
