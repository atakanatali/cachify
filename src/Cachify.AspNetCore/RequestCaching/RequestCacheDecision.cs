using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Buffers;
using System.Text;
using System.Buffers.Binary;
using Cachify.AspNetCore;

namespace Cachify.AspNetCore;

/// <summary>
/// Represents the resolved cache decision settings for a single request.
/// </summary>
internal sealed class RequestCacheDecision
{
    /// <summary>
    /// Gets or sets the request cache mode.
    /// </summary>
    public RequestCacheMode Mode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether request caching is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request can be cached.
    /// </summary>
    public bool CanCache { get; set; }

    /// <summary>
    /// Gets or sets the cache key derived from the request.
    /// </summary>
    public string? CacheKey { get; set; }

    /// <summary>
    /// Gets or sets the computed similarity data for the request.
    /// </summary>
    public SimilarityRequestData? SimilarityRequest { get; set; }

    /// <summary>
    /// Gets or sets the duration for cached responses.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the HTTP methods eligible for caching.
    /// </summary>
    public ISet<string> CacheableMethods { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a value indicating whether authenticated responses can be cached.
    /// </summary>
    public bool CacheAuthenticatedResponses { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether request Cache-Control headers are respected.
    /// </summary>
    public bool RespectRequestCacheControl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether response Cache-Control headers are respected.
    /// </summary>
    public bool RespectResponseCacheControl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Set-Cookie responses can be cached.
    /// </summary>
    public bool AllowSetCookieResponses { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether response buffering is enabled.
    /// </summary>
    public bool EnableResponseBuffering { get; set; }

    /// <summary>
    /// Gets or sets the maximum buffered response body size in bytes.
    /// </summary>
    public long MaxResponseBodySizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the maximum request body size to read for cache key generation.
    /// </summary>
    public long MaxRequestBodySizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the allowed request content types.
    /// </summary>
    public ISet<string> AllowedRequestContentTypes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the allowed response content types.
    /// </summary>
    public ISet<string> AllowedResponseContentTypes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the HTTP status codes eligible for caching.
    /// </summary>
    public ISet<int> CacheableStatusCodes { get; set; } = new HashSet<int>();

    /// <summary>
    /// Gets the included path prefixes for caching.
    /// </summary>
    public IList<PathString> IncludedPaths { get; } = new List<PathString>();

    /// <summary>
    /// Gets the excluded path prefixes for caching.
    /// </summary>
    public IList<PathString> ExcludedPaths { get; } = new List<PathString>();

    /// <summary>
    /// Gets or sets the cache key composition settings.
    /// </summary>
    public RequestCacheKeyOptions KeyOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets similarity cache options.
    /// </summary>
    public SimilarityRequestCacheOptions SimilarityOptions { get; set; } = new();
}
