namespace Cachify.AspNetCore;

/// <summary>
/// Captures compact request features used for similarity scoring.
/// </summary>
public readonly record struct SimilarityRequestFeatures(ulong Signature, int TokenCount, ulong HashPrefix);
