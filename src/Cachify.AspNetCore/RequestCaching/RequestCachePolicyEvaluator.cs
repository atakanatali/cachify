using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cachify.AspNetCore;

internal sealed class RequestCachePolicyEvaluator : IRequestCachePolicyEvaluator
{
    private readonly RequestCacheOptions _options;
    private readonly IRequestCacheKeyBuilder _keyBuilder;
    private readonly ISimilarityRequestHandler _similarityHandler;

    public RequestCachePolicyEvaluator(
        IOptions<RequestCacheOptions> options,
        IRequestCacheKeyBuilder keyBuilder,
        ISimilarityRequestHandler similarityHandler)
    {
        _options = options.Value;
        _keyBuilder = keyBuilder;
        _similarityHandler = similarityHandler;
    }

    /// <inheritdoc />
    public RequestCacheDecision ResolvePolicy(RequestCachePolicy? policy)
    {
        var keyOptions = new RequestCacheKeyOptions
        {
            IncludeMethod = _options.KeyOptions.IncludeMethod,
            IncludePath = _options.KeyOptions.IncludePath,
            IncludeQueryString = _options.KeyOptions.IncludeQueryString,
            IncludeHeaders = _options.KeyOptions.IncludeHeaders,
            IncludeBody = policy?.IncludeRequestBody ?? _options.KeyOptions.IncludeBody,
            NormalizePathToLowercase = _options.KeyOptions.NormalizePathToLowercase
        };

        var varyHeaders = policy?.VaryByHeaders?.Count > 0
            ? new HashSet<string>(policy.VaryByHeaders, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(_options.KeyOptions.VaryByHeaders, StringComparer.OrdinalIgnoreCase);

        keyOptions.VaryByHeaders.Clear();
        foreach (var header in varyHeaders)
        {
            keyOptions.VaryByHeaders.Add(header);
        }

        var cacheableMethods = policy?.CacheableMethods?.Count > 0
            ? new HashSet<string>(policy.CacheableMethods, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(_options.CacheableMethods, StringComparer.OrdinalIgnoreCase);

        var allowedRequestContentTypes = policy?.AllowedRequestContentTypes?.Count > 0
            ? new HashSet<string>(policy.AllowedRequestContentTypes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(_options.AllowedRequestContentTypes, StringComparer.OrdinalIgnoreCase);

        var allowedResponseContentTypes = policy?.AllowedResponseContentTypes?.Count > 0
            ? new HashSet<string>(policy.AllowedResponseContentTypes, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(_options.AllowedResponseContentTypes, StringComparer.OrdinalIgnoreCase);

        var cacheableStatusCodes = policy?.CacheableStatusCodes?.Count > 0
            ? new HashSet<int>(policy.CacheableStatusCodes)
            : new HashSet<int>(_options.CacheableStatusCodes);

        var decision = new RequestCacheDecision
        {
            Mode = policy?.Mode ?? _options.Mode,
            Enabled = policy?.Enabled ?? _options.Enabled,
            Duration = policy?.Duration ?? _options.DefaultDuration,
            CacheableMethods = cacheableMethods,
            CacheAuthenticatedResponses = policy?.CacheAuthenticatedResponses ?? _options.CacheAuthenticatedResponses,
            RespectRequestCacheControl = policy?.RespectRequestCacheControl ?? _options.RespectRequestCacheControl,
            RespectResponseCacheControl = policy?.RespectResponseCacheControl ?? _options.RespectResponseCacheControl,
            EnableResponseBuffering = policy?.EnableResponseBuffering ?? _options.EnableResponseBuffering,
            MaxResponseBodySizeBytes = policy?.MaxResponseBodySizeBytes ?? _options.MaxResponseBodySizeBytes,
            MaxRequestBodySizeBytes = policy?.MaxRequestBodySizeBytes ?? _options.MaxRequestBodySizeBytes,
            AllowedRequestContentTypes = allowedRequestContentTypes,
            AllowedResponseContentTypes = allowedResponseContentTypes,
            CacheableStatusCodes = cacheableStatusCodes,
            AllowSetCookieResponses = policy?.AllowSetCookieResponses ?? _options.AllowSetCookieResponses,
            KeyOptions = keyOptions,
            SimilarityOptions = _options.Similarity
        };

        foreach (var path in _options.IncludedPaths)
        {
            decision.IncludedPaths.Add(path);
        }

        foreach (var path in _options.ExcludedPaths)
        {
            decision.ExcludedPaths.Add(path);
        }

        return decision;
    }

    /// <inheritdoc />
    public async Task<RequestCacheDecision> EvaluateRequestAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken)
    {
        var request = context.Request;

        if (!decision.CacheableMethods.Contains(request.Method))
        {
            decision.CanCache = false;
            return decision;
        }

        if (!IsPathAllowed(request.Path, decision))
        {
            decision.CanCache = false;
            return decision;
        }

        if (!IsRequestContentTypeAllowed(request.ContentType, decision))
        {
            decision.CanCache = false;
            return decision;
        }

        if (!decision.CacheAuthenticatedResponses && request.Headers.ContainsKey("Authorization"))
        {
            decision.CanCache = false;
            return decision;
        }

        if (decision.RespectRequestCacheControl && HasNoStoreDirective(request.Headers.CacheControl))
        {
            decision.CanCache = false;
            return decision;
        }

        if (decision.Mode == RequestCacheMode.Similarity)
        {
            if (!decision.SimilarityOptions.Enabled)
            {
                decision.CanCache = false;
                return decision;
            }

            decision.SimilarityRequest = await _similarityHandler.BuildSimilarityRequestAsync(context, decision, cancellationToken).ConfigureAwait(false);
            decision.CacheKey = decision.SimilarityRequest?.CacheKey;
        }
        else
        {
            decision.CacheKey = await _keyBuilder.BuildCacheKeyAsync(context, decision, cancellationToken).ConfigureAwait(false);
        }

        decision.CanCache = decision.CacheKey is not null;
        return decision;
    }

    private bool IsPathAllowed(PathString path, RequestCacheDecision decision)
    {
        if (decision.IncludedPaths.Count > 0 && decision.IncludedPaths.All(prefix => !path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (decision.ExcludedPaths.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static bool IsRequestContentTypeAllowed(string? contentType, RequestCacheDecision decision)
    {
        if (decision.AllowedRequestContentTypes.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return decision.AllowedRequestContentTypes.Any(allowed => contentType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasNoStoreDirective(string? cacheControl)
    {
        if (string.IsNullOrWhiteSpace(cacheControl))
        {
            return false;
        }

        return cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase)
            || cacheControl.Contains("no-cache", StringComparison.OrdinalIgnoreCase)
            || cacheControl.Contains("private", StringComparison.OrdinalIgnoreCase);
    }
}
