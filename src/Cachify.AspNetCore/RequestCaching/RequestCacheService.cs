using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    private static readonly Meter Meter = new("Cachify");
    private static readonly Counter<long> SimilarityCacheHitTotal = Meter.CreateCounter<long>("similarity_cache_hit");
    private static readonly Counter<long> SimilarityCacheMissTotal = Meter.CreateCounter<long>("similarity_cache_miss");
    private static readonly Counter<long> SimilarityCandidatesCount = Meter.CreateCounter<long>("similarity_candidates_count");
    private static readonly Histogram<double> SimilarityBestScoreHistogram = Meter.CreateHistogram<double>("similarity_best_score_histogram");

    private const string ExecutionItemKey = "Cachify.RequestCache.Executed";
    private const string MetadataWriterItemKey = "Cachify.RequestCache.MetadataWriter";

    private readonly ICacheService _cache;
    private readonly RequestCacheOptions _options;
    private readonly ILogger<RequestCacheService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IRequestCanonicalizer _canonicalizer;
    private readonly IRequestHasher _hasher;
    private readonly SimHashSignatureBuilder _signatureBuilder;
    private readonly ISimilarityScorer _similarityScorer;
    private readonly ISimilarityRequestIndex _similarityIndex;
    private readonly IEmbeddingSimilarityScorer? _embeddingScorer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestCacheService"/> class.
    /// </summary>
    public RequestCacheService(
        ICacheService cache,
        IOptions<RequestCacheOptions> options,
        ILogger<RequestCacheService> logger,
        IRequestCanonicalizer canonicalizer,
        IRequestHasher hasher,
        SimHashSignatureBuilder signatureBuilder,
        ISimilarityScorer similarityScorer,
        ISimilarityRequestIndex similarityIndex,
        IEmbeddingSimilarityScorer? embeddingScorer = null,
        TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _canonicalizer = canonicalizer;
        _hasher = hasher;
        _signatureBuilder = signatureBuilder;
        _similarityScorer = similarityScorer;
        _similarityIndex = similarityIndex;
        _embeddingScorer = embeddingScorer;
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
        var resolvedPolicy = ResolvePolicy(policy);

        if (!resolvedPolicy.Enabled)
        {
            await next(cancellationToken).ConfigureAwait(false);
            return;
        }

        var decision = await EvaluateRequestAsync(context, resolvedPolicy, cancellationToken).ConfigureAwait(false);
        if (!decision.CanCache)
        {
            await next(cancellationToken).ConfigureAwait(false);
            return;
        }

        var lookup = await TryGetCachedEntryAsync(context, decision, cancellationToken).ConfigureAwait(false);
        if (lookup.Entry is not null)
        {
            ApplyCachedResponse(context, lookup.Entry, decision, lookup.SimilarityScore);
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

        var entry = BuildCacheEntry(context, responseBody, decision);
        await _cache.SetAsync(decision.CacheKey!, entry, new CacheEntryOptions { TimeToLive = decision.Duration }, cancellationToken)
            .ConfigureAwait(false);

        if (decision.Mode == RequestCacheMode.Similarity && decision.SimilarityRequest is not null)
        {
            AddSimilarityIndexEntry(decision, entry);
        }

        EmitMetadata(context, isHit: false, isStale: false, decision.CacheKey!, entry.CachedAt, decision.Duration, similarityScore: null);
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
        var resolvedPolicy = ResolvePolicy(policy);
        if (!resolvedPolicy.Enabled)
        {
            return null;
        }

        var decision = await EvaluateRequestAsync(context, resolvedPolicy, cancellationToken).ConfigureAwait(false);
        if (!decision.CanCache)
        {
            return null;
        }

        var lookup = await TryGetCachedEntryAsync(context, decision, cancellationToken).ConfigureAwait(false);
        return lookup.Entry;
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
        var resolvedPolicy = ResolvePolicy(policy);
        if (!resolvedPolicy.Enabled)
        {
            return;
        }

        var decision = await EvaluateRequestAsync(context, resolvedPolicy, cancellationToken).ConfigureAwait(false);
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

        if (decision.Mode == RequestCacheMode.Similarity && decision.SimilarityRequest is not null)
        {
            AddSimilarityIndexEntry(decision, entry);
        }
    }

    /// <summary>
    /// Resolves the effective cache policy by merging global options with per-request overrides.
    /// </summary>
    /// <param name="policy">The optional per-request policy overrides.</param>
    /// <returns>The resolved decision model used for request evaluation.</returns>
    private RequestCacheDecision ResolvePolicy(RequestCachePolicy? policy)
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

    /// <summary>
    /// Evaluates whether the incoming request is eligible for caching and builds a cache key.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision updated with eligibility and cache key information.</returns>
    private async Task<RequestCacheDecision> EvaluateRequestAsync(
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

            decision.SimilarityRequest = await BuildSimilarityRequestAsync(context, decision, cancellationToken).ConfigureAwait(false);
            decision.CacheKey = decision.SimilarityRequest?.CacheKey;
        }
        else
        {
            decision.CacheKey = await BuildCacheKeyAsync(context, decision, cancellationToken).ConfigureAwait(false);
        }

        decision.CanCache = decision.CacheKey is not null;
        return decision;
    }

    /// <summary>
    /// Attempts to retrieve a cached entry using exact or similarity-based matching.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lookup result containing any cached entry and similarity score.</returns>
    private async Task<SimilarityCacheLookupResult> TryGetCachedEntryAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.CacheKey is null)
        {
            return new SimilarityCacheLookupResult(null, null);
        }

        if (decision.Mode != RequestCacheMode.Similarity)
        {
            var entry = await _cache.GetAsync<RequestCacheEntry>(decision.CacheKey, cancellationToken).ConfigureAwait(false);
            return new SimilarityCacheLookupResult(entry, null);
        }

        if (decision.SimilarityRequest is null)
        {
            return new SimilarityCacheLookupResult(null, null);
        }

        var exactEntry = await _cache.GetAsync<RequestCacheEntry>(decision.CacheKey, cancellationToken).ConfigureAwait(false);
        if (exactEntry is not null)
        {
            RecordSimilarityObservation(bestScore: 1d, candidates: 0, servedFromCache: true);
            return new SimilarityCacheLookupResult(exactEntry, 1d);
        }

        if (!ShouldAttemptSimilarityLookup(context, decision))
        {
            RecordSimilarityObservation(bestScore: null, candidates: 0, servedFromCache: false);
            return new SimilarityCacheLookupResult(null, null);
        }

        var options = decision.SimilarityOptions;
        var candidates = _similarityIndex.GetCandidates(decision.SimilarityRequest.Features.Signature, options.MaxCandidates);

        var now = _timeProvider.GetUtcNow();
        var bestScore = 0d;
        SimilarityIndexEntry? bestEntry = null;

        foreach (var candidate in candidates)
        {
            if (now - candidate.CachedAt > options.MaxEntryAge)
            {
                _similarityIndex.Remove(candidate.CacheKey);
                continue;
            }

            var score = ScoreSimilarity(decision.SimilarityRequest, candidate);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestEntry = candidate;
        }

        if (bestEntry is not null && bestScore >= options.MinSimilarity)
        {
            var entry = await _cache.GetAsync<RequestCacheEntry>(bestEntry.CacheKey, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                _similarityIndex.AddOrUpdate(bestEntry);
                RecordSimilarityObservation(bestScore, candidates.Count, servedFromCache: true);
                return new SimilarityCacheLookupResult(entry, bestScore);
            }

            _similarityIndex.Remove(bestEntry.CacheKey);
        }

        RecordSimilarityObservation(bestScore, candidates.Count, servedFromCache: false);
        return new SimilarityCacheLookupResult(null, null);
    }

    /// <summary>
    /// Builds a canonical cache key from request method/path/query/headers/body as configured.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The canonical cache key or <c>null</c> when it cannot be created.</returns>
    private async Task<string?> BuildCacheKeyAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var builder = new StringBuilder();

        if (decision.KeyOptions.IncludeMethod)
        {
            builder.Append(request.Method);
        }

        if (decision.KeyOptions.IncludePath)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var path = request.Path.Value ?? string.Empty;
            if (decision.KeyOptions.NormalizePathToLowercase)
            {
                path = path.ToLowerInvariant();
            }

            builder.Append(path);
        }

        if (decision.KeyOptions.IncludeQueryString)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var queryPairs = request.Query
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(pair => pair.Value.Select(value => (pair.Key, Value: value)))
                .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            var queryBuilder = new StringBuilder();
            foreach (var (key, value) in queryPairs)
            {
                if (queryBuilder.Length > 0)
                {
                    queryBuilder.Append('&');
                }

                queryBuilder.Append(key).Append('=').Append(value);
            }

            builder.Append(queryBuilder);
        }

        if (decision.KeyOptions.IncludeHeaders)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var headerBuilder = new StringBuilder();
            foreach (var header in decision.KeyOptions.VaryByHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
            {
                if (!request.Headers.TryGetValue(header, out var values))
                {
                    continue;
                }

                if (headerBuilder.Length > 0)
                {
                    headerBuilder.Append('&');
                }

                headerBuilder.Append(header.ToLowerInvariant()).Append('=');
                headerBuilder.Append(string.Join(',', values.Select(v => v.Trim())));
            }

            builder.Append(headerBuilder);
        }

        if (decision.KeyOptions.IncludeBody)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var bodyHash = await ReadRequestBodyHashAsync(request, decision.MaxRequestBodySizeBytes, cancellationToken).ConfigureAwait(false);
            if (bodyHash is null)
            {
                return null;
            }

            builder.Append(bodyHash);
        }

        var canonical = builder.ToString();
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"http:req:{Convert.ToHexString(hash)}";
    }

    /// <summary>
    /// Builds similarity request data, including a canonical payload, hash, and signature.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The similarity request data or <c>null</c> when it cannot be created.</returns>
    private async Task<SimilarityRequestData?> BuildSimilarityRequestAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken)
    {
        var payload = await BuildSimilarityPayloadAsync(context, decision, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        if (payload.Length > decision.SimilarityOptions.MaxCanonicalLength)
        {
            return null;
        }

        var hash = _hasher.ComputeHash(payload);
        var hashPrefix = hash.Length >= sizeof(ulong)
            ? BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan())
            : 0UL;
        var cacheKey = $"http:req:sim:{Convert.ToHexString(hash)}";

        var (signature, tokenCount) = _signatureBuilder.BuildSignature(payload, decision.SimilarityOptions.MaxTokens);
        var features = new SimilarityRequestFeatures(signature, tokenCount, hashPrefix);
        var embedding = await BuildEmbeddingAsync(payload, decision.SimilarityOptions, cancellationToken).ConfigureAwait(false);

        return new SimilarityRequestData(cacheKey, features, embedding);
    }

    /// <summary>
    /// Builds a canonical payload string for similarity evaluation.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The cache decision settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The canonical payload string or <c>null</c> if unavailable.</returns>
    private async Task<string?> BuildSimilarityPayloadAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var options = decision.SimilarityOptions.KeyOptions;
        var builder = new StringBuilder();

        if (options.IncludeMethod)
        {
            builder.Append(request.Method);
        }

        if (options.IncludePath)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var path = request.Path.Value ?? string.Empty;
            if (options.NormalizePathToLowercase)
            {
                path = path.ToLowerInvariant();
            }

            builder.Append(path);
        }

        if (options.IncludeQueryString)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var queryPairs = request.Query
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(pair => pair.Value.Select(value => (pair.Key, Value: value)))
                .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            var queryBuilder = new StringBuilder();
            foreach (var (key, value) in queryPairs)
            {
                if (queryBuilder.Length > 0)
                {
                    queryBuilder.Append('&');
                }

                queryBuilder.Append(key).Append('=').Append(value);
            }

            builder.Append(queryBuilder);
        }

        if (options.IncludeHeaders)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var headerBuilder = new StringBuilder();
            foreach (var header in options.VaryByHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
            {
                if (!request.Headers.TryGetValue(header, out var values))
                {
                    continue;
                }

                if (headerBuilder.Length > 0)
                {
                    headerBuilder.Append('&');
                }

                headerBuilder.Append(header.ToLowerInvariant()).Append('=');
                headerBuilder.Append(string.Join(',', values.Select(v => v.Trim())));
            }

            builder.Append(headerBuilder);
        }

        if (options.IncludeBody)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var body = await ReadRequestBodyAsync(request, decision.MaxRequestBodySizeBytes, cancellationToken).ConfigureAwait(false);
            if (body is null)
            {
                return null;
            }

            var canonical = _canonicalizer.Canonicalize(body, request.ContentType);
            if (canonical is null)
            {
                return null;
            }

            builder.Append(canonical);
        }

        var payload = builder.ToString();
        return string.IsNullOrWhiteSpace(payload) ? null : payload;
    }

    /// <summary>
    /// Builds an optional embedding for the payload when configured.
    /// </summary>
    /// <param name="payload">The canonical payload.</param>
    /// <param name="options">The similarity cache options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The embedding vector or <c>null</c> when unavailable.</returns>
    private async ValueTask<ReadOnlyMemory<float>?> BuildEmbeddingAsync(
        string payload,
        SimilarityRequestCacheOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.UseEmbeddingScorer || _embeddingScorer is null)
        {
            return null;
        }

        var embedding = await _embeddingScorer.CreateEmbeddingAsync(payload, cancellationToken).ConfigureAwait(false);
        if (embedding.IsEmpty)
        {
            return null;
        }

        if (embedding.Length > options.MaxEmbeddingLength)
        {
            embedding = embedding.Slice(0, options.MaxEmbeddingLength);
        }

        return embedding;
    }

    /// <summary>
    /// Determines whether similarity lookups should be attempted for the current request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The cache decision settings.</param>
    /// <returns><c>true</c> if similarity lookup is permitted; otherwise, <c>false</c>.</returns>
    private static bool ShouldAttemptSimilarityLookup(HttpContext context, RequestCacheDecision decision)
    {
        var options = decision.SimilarityOptions;
        if (!options.Enabled)
        {
            return false;
        }

        if (options.OnlyIfCostly is not null && !options.OnlyIfCostly(context))
        {
            return false;
        }

        if (options.RequiredHeaders.Count > 0
            && options.RequiredHeaders.Any(header => !context.Request.Headers.ContainsKey(header)))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Scores similarity between the incoming request and a candidate entry.
    /// </summary>
    /// <param name="request">The current request data.</param>
    /// <param name="candidate">The candidate similarity entry.</param>
    /// <returns>The similarity score.</returns>
    private double ScoreSimilarity(SimilarityRequestData request, SimilarityIndexEntry candidate)
    {
        if (_embeddingScorer is not null
            && request.Embedding is not null
            && candidate.Embedding is not null)
        {
            return _embeddingScorer.Score(request.Embedding.Value, candidate.Embedding.Value);
        }

        var features = new SimilarityRequestFeatures(candidate.Signature, candidate.TokenCount, candidate.HashPrefix);
        return _similarityScorer.Score(request.Features, features);
    }

    /// <summary>
    /// Adds a similarity index entry for the cached response.
    /// </summary>
    /// <param name="decision">The cache decision settings.</param>
    /// <param name="entry">The cached response entry.</param>
    private void AddSimilarityIndexEntry(RequestCacheDecision decision, RequestCacheEntry entry)
    {
        if (decision.SimilarityRequest is null)
        {
            return;
        }

        var features = decision.SimilarityRequest.Value.Features;
        var indexEntry = new SimilarityIndexEntry
        {
            CacheKey = decision.CacheKey!,
            Signature = features.Signature,
            TokenCount = features.TokenCount,
            HashPrefix = features.HashPrefix,
            CachedAt = entry.CachedAt,
            Embedding = decision.SimilarityRequest.Value.Embedding
        };

        _similarityIndex.AddOrUpdate(indexEntry);
    }

    /// <summary>
    /// Records similarity cache telemetry and activity tags.
    /// </summary>
    /// <param name="bestScore">The best similarity score achieved.</param>
    /// <param name="candidates">The number of candidates scored.</param>
    /// <param name="servedFromCache">Whether a cached response was served.</param>
    private static void RecordSimilarityObservation(double? bestScore, int candidates, bool servedFromCache)
    {
        if (servedFromCache)
        {
            SimilarityCacheHitTotal.Add(1);
        }
        else
        {
            SimilarityCacheMissTotal.Add(1);
        }

        if (candidates > 0)
        {
            SimilarityCandidatesCount.Add(candidates);
        }

        if (bestScore.HasValue)
        {
            SimilarityBestScoreHistogram.Record(bestScore.Value);
        }

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("mode", "similarity");
            activity.SetTag("served_from_cache", servedFromCache);
            if (bestScore.HasValue)
            {
                activity.SetTag("best_score", bestScore.Value);
            }
        }
    }

    /// <summary>
    /// Computes a SHA-256 hash of the request body up to a configured size limit.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="maxBytes">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The hex-encoded hash or <c>null</c> when the body exceeds the limit.</returns>
    private static async Task<string?> ReadRequestBodyHashAsync(
        HttpRequest request,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (request.Body is null || !request.Body.CanRead)
        {
            return string.Empty;
        }

        request.EnableBuffering();
        request.Body.Position = 0;

        using var sha = SHA256.Create();
        var buffer = new byte[8192];
        long totalRead = 0;
        int read;

        while ((read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalRead += read;
            if (totalRead > maxBytes)
            {
                request.Body.Position = 0;
                return null;
            }

            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        request.Body.Position = 0;

        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
    }

    /// <summary>
    /// Reads the request body as a UTF-8 string up to a configured size limit.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="maxBytes">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request body string or <c>null</c> when the limit is exceeded.</returns>
    private static async Task<string?> ReadRequestBodyAsync(
        HttpRequest request,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (request.Body is null || !request.Body.CanRead)
        {
            return string.Empty;
        }

        request.EnableBuffering();
        request.Body.Position = 0;

        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        long totalRead = 0;

        try
        {
            using var stream = new MemoryStream();
            int read;
            while ((read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalRead += read;
                if (totalRead > maxBytes)
                {
                    request.Body.Position = 0;
                    return null;
                }

                stream.Write(buffer, 0, read);
            }

            request.Body.Position = 0;
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Determines whether the request path passes include/exclude rules.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <returns><c>true</c> if the path is eligible; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Determines whether the request content type is eligible for caching.
    /// </summary>
    /// <param name="contentType">The request content type.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <returns><c>true</c> if the content type is eligible; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Determines whether the response can be stored in the cache based on headers and status code.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <returns><c>true</c> if the response is eligible for caching; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Builds a cache entry from the current response and captured body.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="responseBody">The response body to cache.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    /// <returns>The cache entry payload.</returns>
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

            headers[header.Key] = header.Value.ToArray();
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

    /// <summary>
    /// Writes a cached response entry back to the HTTP response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="entry">The cached response entry.</param>
    /// <param name="decision">The resolved cache decision settings.</param>
    private void ApplyCachedResponse(
        HttpContext context,
        RequestCacheEntry entry,
        RequestCacheDecision decision,
        double? similarityScore)
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
            context.Response.Body.Write(entry.Body, 0, entry.Body.Length);
        }
    }

    /// <summary>
    /// Records cache metadata on the HTTP context for observability and header emission.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="isHit">Whether the response was served from cache.</param>
    /// <param name="isStale">Whether the cached response is stale.</param>
    /// <param name="cacheKey">The cache key used.</param>
    /// <param name="cachedAt">The timestamp when the response was cached.</param>
    /// <param name="duration">The logical cache duration.</param>
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

    /// <summary>
    /// Registers a response OnStarting callback to emit cache metadata headers.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    private void EnsureMetadataWriter(HttpContext context)
    {
        var headers = _options.ResponseHeaders;
        if (!headers.Enabled || context.Items.ContainsKey(MetadataWriterItemKey))
        {
            return;
        }

        context.Items[MetadataWriterItemKey] = true;
        context.Response.OnStarting(() =>
        {
            if (!RequestCacheMetadataAccessor.TryGetMetadata(context, out var metadata))
            {
                return Task.CompletedTask;
            }

            context.Response.Headers[headers.CacheStatusHeader] = metadata.IsHit ? "HIT" : "MISS";
            context.Response.Headers[headers.CacheStaleHeader] = metadata.IsStale ? "true" : "false";

            if (metadata.SimilarityScore is not null)
            {
                context.Response.Headers[headers.SimilarityHeader] = metadata.SimilarityScore.Value.ToString("F3", CultureInfo.InvariantCulture);
            }

            if (headers.IncludeCacheKey)
            {
                context.Response.Headers[headers.CacheKeyHeader] = metadata.CacheKey;
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Determines whether Cache-Control headers indicate a response should not be cached.
    /// </summary>
    /// <param name="cacheControl">The Cache-Control header value.</param>
    /// <returns><c>true</c> if caching should be bypassed; otherwise, <c>false</c>.</returns>
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

    /// <summary>
    /// Represents the computed similarity data for a request.
    /// </summary>
    private readonly record struct SimilarityRequestData(
        string CacheKey,
        SimilarityRequestFeatures Features,
        ReadOnlyMemory<float>? Embedding);

    /// <summary>
    /// Represents the result of a cache lookup.
    /// </summary>
    private readonly record struct SimilarityCacheLookupResult(RequestCacheEntry? Entry, double? SimilarityScore);

    /// <summary>
    /// Represents the resolved cache decision settings for a single request.
    /// </summary>
    private sealed class RequestCacheDecision
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
}
