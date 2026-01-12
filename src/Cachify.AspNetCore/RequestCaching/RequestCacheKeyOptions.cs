namespace Cachify.AspNetCore;

/// <summary>
/// Controls how cache keys are derived from HTTP requests.
/// </summary>
public sealed class RequestCacheKeyOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the HTTP method is included in the cache key.
    /// </summary>
    public bool IncludeMethod { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the request path is included in the cache key.
    /// </summary>
    public bool IncludePath { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the query string is included in the cache key.
    /// </summary>
    public bool IncludeQueryString { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether request headers are included in the cache key.
    /// </summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the request body is included in the cache key.
    /// </summary>
    public bool IncludeBody { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request path is normalized to lowercase.
    /// </summary>
    public bool NormalizePathToLowercase { get; set; } = true;

    /// <summary>
    /// Gets the list of header names to include in cache key generation.
    /// </summary>
    public ISet<string> VaryByHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Accept",
        "Accept-Encoding"
    };
}
