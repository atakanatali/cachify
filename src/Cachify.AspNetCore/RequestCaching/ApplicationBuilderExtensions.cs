using Microsoft.AspNetCore.Builder;

namespace Cachify.AspNetCore;

/// <summary>
/// Provides application builder extensions for request caching.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds request caching middleware to the pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    public static IApplicationBuilder UseRequestCaching(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestCachingMiddleware>();
    }
}
