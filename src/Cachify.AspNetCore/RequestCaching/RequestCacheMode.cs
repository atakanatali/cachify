namespace Cachify.AspNetCore;

/// <summary>
/// Defines the request caching strategy used by the request cache pipeline.
/// </summary>
public enum RequestCacheMode
{
    /// <summary>
    /// Uses exact cache key matching for request/response caching.
    /// </summary>
    Exact,

    /// <summary>
    /// Uses similarity-based matching to serve cached responses for near-duplicate requests.
    /// </summary>
    Similarity
}
