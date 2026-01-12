namespace Cachify.Abstractions;

/// <summary>
/// Builds cache keys from logical parts.
/// </summary>
public interface ICacheKeyBuilder
{
    /// <summary>
    /// Builds a cache key from the provided parts.
    /// </summary>
    /// <param name="key">The base key.</param>
    /// <param name="region">An optional region.</param>
    /// <param name="prefix">An optional prefix.</param>
    string Build(string key, string? region = null, string? prefix = null);
}
