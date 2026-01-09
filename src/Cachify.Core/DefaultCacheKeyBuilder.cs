using Cachify.Abstractions;

namespace Cachify.Core;

public sealed class DefaultCacheKeyBuilder : ICacheKeyBuilder
{
    public string Build(string key, string? region = null, string? prefix = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            parts.Add(prefix!);
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            parts.Add(region!);
        }

        parts.Add(key);

        return string.Join(':', parts);
    }
}
