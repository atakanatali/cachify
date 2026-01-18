using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Cachify.Abstractions;

namespace Cachify.AspNetCore;

internal sealed class SimilarityRequestHandler : ISimilarityRequestHandler
{
    private static readonly Meter Meter = new("Cachify");
    private static readonly Counter<long> SimilarityCacheHitTotal = Meter.CreateCounter<long>("similarity_cache_hit");
    private static readonly Counter<long> SimilarityCacheMissTotal = Meter.CreateCounter<long>("similarity_cache_miss");
    private static readonly Counter<long> SimilarityCandidatesCount = Meter.CreateCounter<long>("similarity_candidates_count");
    private static readonly Histogram<double> SimilarityBestScoreHistogram = Meter.CreateHistogram<double>("similarity_best_score_histogram");

    private readonly ICacheService _cache;
    private readonly IRequestCanonicalizer _canonicalizer;
    private readonly IRequestHasher _hasher;
    private readonly SimHashSignatureBuilder _signatureBuilder;
    private readonly ISimilarityScorer _similarityScorer;
    private readonly ISimilarityRequestIndex _similarityIndex;
    private readonly IEmbeddingSimilarityScorer? _embeddingScorer;
    private readonly TimeProvider _timeProvider;

    public SimilarityRequestHandler(
        ICacheService cache,
        IRequestCanonicalizer canonicalizer,
        IRequestHasher hasher,
        SimHashSignatureBuilder signatureBuilder,
        ISimilarityScorer similarityScorer,
        ISimilarityRequestIndex similarityIndex,
        IEmbeddingSimilarityScorer? embeddingScorer = null,
        TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _canonicalizer = canonicalizer;
        _hasher = hasher;
        _signatureBuilder = signatureBuilder;
        _similarityScorer = similarityScorer;
        _similarityIndex = similarityIndex;
        _embeddingScorer = embeddingScorer;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<SimilarityRequestData?> BuildSimilarityRequestAsync(
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

    /// <inheritdoc />
    public async Task<SimilarityCacheLookupResult> FindMatchAsync(
        HttpContext context,
        RequestCacheDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.SimilarityRequest is null)
        {
            return new SimilarityCacheLookupResult(null, null);
        }

        // 1. Try exact match first
        // Note: decision.CacheKey should be set to SimilarityRequest.CacheKey by the caller/evaluator
        var cacheKey = decision.CacheKey ?? decision.SimilarityRequest.Value.CacheKey;
        var exactEntry = await _cache.GetAsync<RequestCacheEntry>(cacheKey, cancellationToken).ConfigureAwait(false);
        if (exactEntry is not null)
        {
            RecordSimilarityObservation(bestScore: 1d, candidates: 0, servedFromCache: true);
            return new SimilarityCacheLookupResult(exactEntry, 1d);
        }

        // 2. Try similarity lookup
        if (!ShouldAttemptSimilarityLookup(context, decision))
        {
            RecordSimilarityObservation(bestScore: null, candidates: 0, servedFromCache: false);
            return new SimilarityCacheLookupResult(null, null);
        }

        var options = decision.SimilarityOptions;
        var similarityRequest = decision.SimilarityRequest.Value;
        var candidates = _similarityIndex.GetCandidates(similarityRequest.Features.Signature, options.MaxCandidates);

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

            var score = ScoreSimilarity(similarityRequest, candidate);
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

    /// <inheritdoc />
    public void AddIndexEntry(RequestCacheDecision decision, RequestCacheEntry entry)
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

                headerBuilder.Append(header!.ToLowerInvariant()).Append('=');
                headerBuilder.Append(string.Join(',', values!.Where(v => v is not null).Select(v => v!.Trim()!)));
            }

            builder.Append(headerBuilder);
        }

        if (options.IncludeBody)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            var body = await RequestUtilities.ReadRequestBodyAsync(request, decision.MaxRequestBodySizeBytes, cancellationToken).ConfigureAwait(false);
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
}
