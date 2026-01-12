namespace Cachify.AspNetCore;

/// <summary>
/// Represents a compact similarity entry stored in the index.
/// </summary>
public sealed class SimilarityIndexEntry
{
    /// <summary>
    /// Gets or sets the cache key used to retrieve the cached response.
    /// </summary>
    public required string CacheKey { get; init; }

    /// <summary>
    /// Gets or sets the request signature.
    /// </summary>
    public required ulong Signature { get; init; }

    /// <summary>
    /// Gets or sets the token count used to build the signature.
    /// </summary>
    public required int TokenCount { get; init; }

    /// <summary>
    /// Gets or sets the canonical hash prefix.
    /// </summary>
    public required ulong HashPrefix { get; init; }

    /// <summary>
    /// Gets or sets the time at which the entry was cached.
    /// </summary>
    public required DateTimeOffset CachedAt { get; init; }

    /// <summary>
    /// Gets or sets the optional embedding vector.
    /// </summary>
    public ReadOnlyMemory<float>? Embedding { get; init; }
}
