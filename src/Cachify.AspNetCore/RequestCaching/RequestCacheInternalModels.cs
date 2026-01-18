using System;

namespace Cachify.AspNetCore;

/// <summary>
/// Represents the computed similarity data for a request.
/// </summary>
internal readonly record struct SimilarityRequestData(
    string CacheKey,
    SimilarityRequestFeatures Features,
    ReadOnlyMemory<float>? Embedding);

/// <summary>
/// Represents the result of a cache lookup.
/// </summary>
internal readonly record struct SimilarityCacheLookupResult(RequestCacheEntry? Entry, double? SimilarityScore);
