using Microsoft.AspNetCore.Http;

namespace Cachify.AspNetCore;

/// <summary>
/// Defines a contract for building canonical cache keys from HTTP requests.
/// </summary>
internal interface IRequestCacheKeyBuilder
{
    /// <summary>
    /// Builds a canonical cache key from request method/path/query/headers/body as configured.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The canonical cache key or <c>null</c> when it cannot be created.</returns>
    Task<string?> BuildCacheKeyAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken);
}
