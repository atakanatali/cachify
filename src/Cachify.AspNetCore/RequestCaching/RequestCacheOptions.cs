using Microsoft.AspNetCore.Http;

namespace Cachify.AspNetCore;

/// <summary>
/// Configures request/response caching behavior for HTTP pipelines.
/// </summary>
public sealed class RequestCacheOptions
{
    /// <summary>
    /// Gets or sets the request caching mode.
    /// </summary>
    public RequestCacheMode Mode { get; set; } = RequestCacheMode.Exact;

    /// <summary>
    /// Gets or sets a value indicating whether request caching is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the default cache duration for cached responses.
    /// </summary>
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the HTTP methods eligible for caching.
    /// </summary>
    public ISet<string> CacheableMethods { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head
    };

    /// <summary>
    /// Gets the list of path prefixes to include. Empty means all paths are included.
    /// </summary>
    public IList<PathString> IncludedPaths { get; } = new List<PathString>();

    /// <summary>
    /// Gets the list of path prefixes to exclude from caching.
    /// </summary>
    public IList<PathString> ExcludedPaths { get; } = new List<PathString>();

    /// <summary>
    /// Gets the request content types eligible for caching. Empty means all request content types are eligible.
    /// </summary>
    public ISet<string> AllowedRequestContentTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the response content types eligible for caching. Empty means all response content types are eligible.
    /// </summary>
    public ISet<string> AllowedResponseContentTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the HTTP status codes eligible for caching.
    /// </summary>
    public ISet<int> CacheableStatusCodes { get; } = new HashSet<int> { StatusCodes.Status200OK };

    /// <summary>
    /// Gets or sets a value indicating whether authenticated responses can be cached.
    /// </summary>
    public bool CacheAuthenticatedResponses { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether request Cache-Control headers are respected.
    /// </summary>
    public bool RespectRequestCacheControl { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether response Cache-Control headers are respected.
    /// </summary>
    public bool RespectResponseCacheControl { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether responses with Set-Cookie headers can be cached.
    /// </summary>
    public bool AllowSetCookieResponses { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether response buffering is enabled to capture cache entries.
    /// </summary>
    public bool EnableResponseBuffering { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum response body size that will be buffered for caching.
    /// </summary>
    public long MaxResponseBodySizeBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum request body size that will be read when building cache keys.
    /// </summary>
    public long MaxRequestBodySizeBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Gets cache key composition settings.
    /// </summary>
    public RequestCacheKeyOptions KeyOptions { get; } = new();

    /// <summary>
    /// Gets header emission settings for cache metadata.
    /// </summary>
    public RequestCacheHeaderOptions ResponseHeaders { get; } = new();

    /// <summary>
    /// Gets similarity caching configuration.
    /// </summary>
    public SimilarityRequestCacheOptions Similarity { get; } = new();
}
