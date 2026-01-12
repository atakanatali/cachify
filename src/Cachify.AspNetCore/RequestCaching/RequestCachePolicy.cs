namespace Cachify.AspNetCore;

/// <summary>
/// Overrides global request cache options for a specific endpoint or execution scope.
/// </summary>
public sealed class RequestCachePolicy
{
    /// <summary>
    /// Gets or sets a value indicating whether caching is enabled for the scope.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets the cache duration for the scope.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether request bodies are included in cache keys.
    /// </summary>
    public bool? IncludeRequestBody { get; set; }

    /// <summary>
    /// Gets or sets the headers to vary the cache key by.
    /// </summary>
    public IReadOnlyCollection<string>? VaryByHeaders { get; set; }

    /// <summary>
    /// Gets or sets the HTTP methods eligible for caching.
    /// </summary>
    public IReadOnlyCollection<string>? CacheableMethods { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether authenticated responses can be cached.
    /// </summary>
    public bool? CacheAuthenticatedResponses { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether request Cache-Control headers are respected.
    /// </summary>
    public bool? RespectRequestCacheControl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether response Cache-Control headers are respected.
    /// </summary>
    public bool? RespectResponseCacheControl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether response buffering is enabled.
    /// </summary>
    public bool? EnableResponseBuffering { get; set; }

    /// <summary>
    /// Gets or sets the maximum response body size that will be buffered for caching.
    /// </summary>
    public long? MaxResponseBodySizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the maximum request body size read for key generation.
    /// </summary>
    public long? MaxRequestBodySizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the request content types eligible for caching.
    /// </summary>
    public IReadOnlyCollection<string>? AllowedRequestContentTypes { get; set; }

    /// <summary>
    /// Gets or sets the response content types eligible for caching.
    /// </summary>
    public IReadOnlyCollection<string>? AllowedResponseContentTypes { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status codes eligible for caching.
    /// </summary>
    public IReadOnlyCollection<int>? CacheableStatusCodes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether responses with Set-Cookie headers can be cached.
    /// </summary>
    public bool? AllowSetCookieResponses { get; set; }
}
