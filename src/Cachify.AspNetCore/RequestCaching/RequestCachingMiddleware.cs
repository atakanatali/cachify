using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cachify.AspNetCore;

/// <summary>
/// ASP.NET Core middleware that applies request/response caching.
/// </summary>
public sealed class RequestCachingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestCacheService _service;
    private readonly ILogger<RequestCachingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestCachingMiddleware"/> class.
    /// </summary>
    public RequestCachingMiddleware(
        RequestDelegate next,
        RequestCacheService service,
        ILogger<RequestCachingMiddleware> logger)
    {
        _next = next;
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Processes an HTTP request and applies request caching as configured.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var policy = endpoint?.Metadata.GetMetadata<RequestCachePolicy>();
        if (policy is null)
        {
            var attribute = endpoint?.Metadata.GetMetadata<RequestCacheAttribute>();
            policy = attribute?.ToPolicy();
        }
        if (policy is not null)
        {
            _logger.LogDebug("Request cache policy found for {Path}", context.Request.Path);
        }

        await _service.ExecuteAsync(context, policy, _next, context.RequestAborted).ConfigureAwait(false);
    }
}
