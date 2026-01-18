using System.Globalization;
using Cachify.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cachify.AspNetCore;

/// <summary>
/// Provides a reusable request/response caching workflow built on the Cachify core.
/// </summary>
public sealed class RequestCacheService
{
    private static readonly HashSet<string> IgnoredResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Content-Length",
        "Date",
        "Keep-Alive",
        "Server",
        "Transfer-Encoding"
    };

    private const string ExecutionItemKey = "Cachify.RequestCache.Executed";
    private const string MetadataWriterItemKey = "Cachify.RequestCache.MetadataWriter";

    private readonly ICacheService _cache;
    private readonly IOptions<RequestCacheOptions> _options;
    private readonly ILogger<RequestCacheService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IRequestCachePolicyEvaluator _evaluator;
    private readonly ISimilarityRequestHandler _similarityHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestCacheService"/> class.
    /// </summary>
    internal RequestCacheService(
        ICacheService cache,
        IOptions<RequestCacheOptions> options,
        ILogger<RequestCacheService> logger,
        IRequestCachePolicyEvaluator evaluator,
        ISimilarityRequestHandler similarityHandler,
        TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
        _evaluator = evaluator;
        _similarityHandler = similarityHandler;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Executes a caching workflow around the provided request delegate.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="policy">Optional per-request policy overrides.</param>
    /// <param name="next">The downstream pipeline delegate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task ExecuteAsync(
        HttpContext context,
        RequestCachePolicy? policy,
        Func<CancellationToken, Task> next,
        CancellationToken cancellationToken = default)
    {
        if (context.Items.ContainsKey(ExecutionItemKey))
        {
            await next(cancellationToken).ConfigureAwait(false);
            return;
        }

        context.Items[ExecutionItemKey] = true;
        var decision = _evaluator.ResolvePolicy(policy);

        if (!decision.Enabled)
        {
            await next(cancellationToken).ConfigureAwait(false);
            return;
        }

        decision = await _evaluator.EvaluateRequestAsync(context, decision, cancellationToken).ConfigureAwait(false);
        if (!decision.CanCache)
        {
            await next(cancellationToken).ConfigureAwait(false);
            return;
        }

        RequestCacheEntry? cachedEntry = null;
        double? similarityScore = null;

        if (decision.Mode == RequestCacheMode.Similarity)
        {
            var lookup = await _similarityHandler.FindMatchAsync(context, decision, cancellationToken).ConfigureAwait(false);
            cachedEntry = lookup.Entry;
            similarityScore = lookup.SimilarityScore;
        }
        else if (decision.CacheKey is not null)
        {
            cachedEntry = await _cache.GetAsync<RequestCacheEntry>(decision.CacheKey, cancellationToken).ConfigureAwait(false);
        }

        if (cachedEntry is not null)
        {
            await ApplyCachedResponseAsync(context, cachedEntry, decision, similarityScore, cancellationToken).ConfigureAwait(false);
            return;
        }

        EnsureMetadataWriter(context);
        EmitMetadata(context, isHit: false, isStale: false, decision.CacheKey!, cachedAt: null, decision.Duration, similarityScore: null);

        if (!decision.EnableResponseBuffering)
        {
            await next(cancellationToken).ConfigureAwait(false);
            return;
        }

        var originalBody = context.Response.Body;
        await using var bufferingStream = new ResponseBufferingStream(originalBody, decision.MaxResponseBodySizeBytes);
        context.Response.Body = bufferingStream;

        try
        {
            await next(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        await bufferingStream.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (!bufferingStream.BufferingEnabled || bufferingStream.HasOverflowed)
        {
            _logger.LogDebug("Request cache response buffering overflowed for {Path}", context.Request.Path);
            return;
        }

        var responseBody = bufferingStream.GetBufferedBytes() ?? Array.Empty<byte>();
        if (!IsResponseCacheable(context, decision))
        {
            return;
        }

        // Generate cache entry
        var newEntry = BuildCacheEntry(context, responseBody, decision);
        
        // Parallelize cache operations? No, safe simple first.
        await _cache.SetAsync(decision.CacheKey!, newEntry, new CacheEntryOptions { TimeToLive = decision.Duration }, cancellationToken)
            .ConfigureAwait(false);

        if (decision.Mode == RequestCacheMode.Similarity)
        {
            _similarityHandler.AddIndexEntry(decision, newEntry);
        }

        EmitMetadata(context, isHit: false, isStale: false, decision.CacheKey!, newEntry.CachedAt, decision.Duration, similarityScore: null);
    }

    /// <summary>
    /// Attempts to read a cached response for the provided request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="policy">Optional per-request policy overrides.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cached entry when available.</returns>
    public async Task<RequestCacheEntry?> GetCachedResponseAsync(
        HttpContext context,
        RequestCachePolicy? policy,
        CancellationToken cancellationToken = default)
    {
        var decision = _evaluator.ResolvePolicy(policy);
        if (!decision.Enabled)
        {
            return null;
        }

        decision = await _evaluator.EvaluateRequestAsync(context, decision, cancellationToken).ConfigureAwait(false);
        if (!decision.CanCache)
        {
            return null;
        }

        if (decision.Mode == RequestCacheMode.Similarity)
        {
            var lookup = await _similarityHandler.FindMatchAsync(context, decision, cancellationToken).ConfigureAwait(false);
            return lookup.Entry;
        }

        if (decision.CacheKey is not null)
        {
            return await _cache.GetAsync<RequestCacheEntry>(decision.CacheKey, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Stores a response payload in the request cache.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="policy">Optional per-request policy overrides.</param>
    /// <param name="entry">The response entry to cache.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task StoreResponseAsync(
        HttpContext context,
        RequestCachePolicy? policy,
        RequestCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        var decision = _evaluator.ResolvePolicy(policy);
        if (!decision.Enabled)
        {
            return;
        }

        decision = await _evaluator.EvaluateRequestAsync(context, decision, cancellationToken).ConfigureAwait(false);
        if (!decision.CanCache)
        {
            return;
        }

        if (!IsResponseCacheable(context, decision))
        {
            return;
        }

        entry.Duration = decision.Duration;
        entry.CachedAt = _timeProvider.GetUtcNow();
        await _cache.SetAsync(decision.CacheKey!, entry, new CacheEntryOptions { TimeToLive = decision.Duration }, cancellationToken)
            .ConfigureAwait(false);

        if (decision.Mode == RequestCacheMode.Similarity)
        {
            _similarityHandler.AddIndexEntry(decision, entry);
        }
    }

    private bool IsResponseCacheable(HttpContext context, RequestCacheDecision decision)
    {
        var response = context.Response;
        if (!decision.CacheableStatusCodes.Contains(response.StatusCode))
        {
            return false;
        }

        if (decision.RespectResponseCacheControl && HasNoStoreDirective(response.Headers.CacheControl))
        {
            return false;
        }

        if (!decision.AllowSetCookieResponses && response.Headers.ContainsKey("Set-Cookie"))
        {
            return false;
        }

        if (decision.AllowedResponseContentTypes.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(response.ContentType))
            {
                return false;
            }

            if (!decision.AllowedResponseContentTypes.Any(allowed => response.ContentType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
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

    private RequestCacheEntry BuildCacheEntry(HttpContext context, byte[] responseBody, RequestCacheDecision decision)
    {
        var response = context.Response;
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
        {
            if (IgnoredResponseHeaders.Contains(header.Key))
            {
                continue;
            }

            headers[header.Key] = header.Value.ToArray()!;
        }

        return new RequestCacheEntry
        {
            StatusCode = response.StatusCode,
            Body = responseBody,
            Headers = headers,
            ContentType = response.ContentType,
            CachedAt = _timeProvider.GetUtcNow(),
            Duration = decision.Duration
        };
    }

    private async Task ApplyCachedResponseAsync(
        HttpContext context,
        RequestCacheEntry entry,
        RequestCacheDecision decision,
        double? similarityScore,
        CancellationToken cancellationToken)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = entry.StatusCode;
        if (!string.IsNullOrWhiteSpace(entry.ContentType))
        {
            context.Response.ContentType = entry.ContentType;
        }

        foreach (var header in entry.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        var isStale = _timeProvider.GetUtcNow() > entry.CachedAt.Add(entry.Duration);
        EnsureMetadataWriter(context);
        EmitMetadata(context, isHit: true, isStale, decision.CacheKey!, entry.CachedAt, entry.Duration, similarityScore);

        if (!HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.ContentLength = entry.Body.Length;
            await context.Response.Body.WriteAsync(entry.Body.AsMemory(0, entry.Body.Length), cancellationToken).ConfigureAwait(false);
        }
    }

    private void EmitMetadata(
        HttpContext context,
        bool isHit,
        bool isStale,
        string cacheKey,
        DateTimeOffset? cachedAt,
        TimeSpan duration,
        double? similarityScore)
    {
        context.Items[RequestCacheMetadataAccessor.ItemKey] = new RequestCacheMetadata(
            isHit,
            isStale,
            similarityScore,
            cacheKey,
            cachedAt ?? _timeProvider.GetUtcNow(),
            duration);
    }

    private void EnsureMetadataWriter(HttpContext context)
    {
        var headers = _options.Value.ResponseHeaders; // Fixed: options is IOptions
        if (!headers.Enabled || context.Items.ContainsKey(MetadataWriterItemKey))
        {
            return;
        }

        var responseHeaders = headers;
        context.Items[MetadataWriterItemKey] = true;
        context.Response.OnStarting(() =>
        {
            if (!RequestCacheMetadataAccessor.TryGetMetadata(context, out var metadata))
            {
                return Task.CompletedTask;
            }

            var cacheStatusHeader = responseHeaders!.CacheStatusHeader ?? throw new InvalidOperationException("CacheStatusHeader is required");
            var cacheStaleHeader = responseHeaders.CacheStaleHeader ?? throw new InvalidOperationException("CacheStaleHeader is required");
            context.Response.Headers[cacheStatusHeader] = metadata!.IsHit ? "HIT" : "MISS";
            context.Response.Headers[cacheStaleHeader] = metadata!.IsStale ? "true" : "false";

            if (metadata!.SimilarityScore is not null)
            {
                var similarityHeader = responseHeaders.SimilarityHeader ?? throw new InvalidOperationException("SimilarityHeader is required");
                context.Response.Headers[similarityHeader] = metadata!.SimilarityScore.Value.ToString("F3", CultureInfo.InvariantCulture);
            }

            if (responseHeaders.IncludeCacheKey)
            {
                var cacheKeyHeader = responseHeaders.CacheKeyHeader ?? throw new InvalidOperationException("CacheKeyHeader is required");
                context.Response.Headers[cacheKeyHeader] = metadata!.CacheKey;
            }

            return Task.CompletedTask;
        });
    }
}
