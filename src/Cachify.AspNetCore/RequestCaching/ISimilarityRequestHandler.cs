using Microsoft.AspNetCore.Http;
using Cachify.Abstractions;

namespace Cachify.AspNetCore;

/// <summary>
/// Handles similarity-based caching operations including request building, indexing, and lookup.
/// </summary>
internal interface ISimilarityRequestHandler
{
    /// <summary>
    /// Builds similarity request data, including a canonical payload, hash, and signature.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The similarity request data or <c>null</c> when it cannot be created.</returns>
    Task<SimilarityRequestData?> BuildSimilarityRequestAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to retrieve a cached entry using similarity-based matching.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lookup result containing any cached entry and similarity score.</returns>
    Task<SimilarityCacheLookupResult> FindMatchAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds a similarity index entry for the cached response.
    /// </summary>
    /// <param name="decision">The cache decision settings.</param>
    /// <param name="entry">The cached response entry.</param>
    void AddIndexEntry(RequestCacheDecision decision, RequestCacheEntry entry);
}
