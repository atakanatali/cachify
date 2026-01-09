using System;

namespace Cachify.Core;

public sealed class CachifyOptions
{
    public string? KeyPrefix { get; set; }

    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    public double JitterRatio { get; set; } = 0.0;

    public bool FailFastOnL2Errors { get; set; }
}
