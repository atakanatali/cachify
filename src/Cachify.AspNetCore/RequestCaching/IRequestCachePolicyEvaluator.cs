using Microsoft.AspNetCore.Http;
using Cachify.AspNetCore;

namespace Cachify.AspNetCore;

/// <summary>
/// Evaluates cache policies and determines if a request is eligible for caching.
/// </summary>
internal interface IRequestCachePolicyEvaluator
{
    /// <summary>
    /// Resolves the effective cache policy by merging global options with per-request overrides.
    /// </summary>
    /// <param name="policy">The optional per-request policy overrides.</param>
    /// <returns>The resolved decision model used for request evaluation.</returns>
    RequestCacheDecision ResolvePolicy(RequestCachePolicy? policy);

    /// <summary>
    /// Evaluates whether the incoming request is eligible for caching and builds a cache key.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision updated with eligibility and cache key information.</returns>
    Task<RequestCacheDecision> EvaluateRequestAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken);
}
