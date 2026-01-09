using System;

namespace Cachify.Abstractions;

public sealed class CacheEntryOptions
{
    public TimeSpan? TimeToLive { get; set; }

    public TimeSpan? SlidingExpiration { get; set; }

    public double? JitterRatio { get; set; }

    public TimeSpan? NegativeCacheTtl { get; set; }

    public string? KeyPrefix { get; set; }

    public string? SerializerName { get; set; }
}
