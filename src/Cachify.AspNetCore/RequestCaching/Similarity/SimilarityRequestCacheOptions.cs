using Microsoft.AspNetCore.Http;

namespace Cachify.AspNetCore;

/// <summary>
/// Configures similarity-based request caching behavior.
/// </summary>
public sealed class SimilarityRequestCacheOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SimilarityRequestCacheOptions"/> class.
    /// </summary>
    public SimilarityRequestCacheOptions()
    {
        KeyOptions.IncludeBody = true;
    }

    /// <summary>
    /// Gets or sets a value indicating whether similarity caching is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the minimum similarity score required to serve a cached response.
    /// </summary>
    public double MinSimilarity { get; set; } = 0.95;

    /// <summary>
    /// Gets or sets the maximum age of an indexed request that can be served.
    /// </summary>
    public TimeSpan MaxEntryAge { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the maximum number of entries kept in the similarity index.
    /// </summary>
    public int MaxIndexEntries { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the maximum number of candidate entries to score per lookup.
    /// </summary>
    public int MaxCandidates { get; set; } = 64;

    /// <summary>
    /// Gets or sets the maximum canonical payload length.
    /// </summary>
    public int MaxCanonicalLength { get; set; } = 16 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of tokens used for signature generation.
    /// </summary>
    public int MaxTokens { get; set; } = 512;

    /// <summary>
    /// Gets the property names to exclude during JSON canonicalization.
    /// </summary>
    public ISet<string> IgnoredJsonFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "timestamp",
        "created_at",
        "updated_at"
    };

    /// <summary>
    /// Gets the headers required for similarity caching to be eligible.
    /// </summary>
    public ISet<string> RequiredHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets an optional predicate that must return true for similarity caching to be used.
    /// </summary>
    public Func<HttpContext, bool>? OnlyIfCostly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether embedding-based scoring should be used when available.
    /// </summary>
    public bool UseEmbeddingScorer { get; set; }

    /// <summary>
    /// Gets or sets the maximum embedding length allowed in the similarity index.
    /// </summary>
    public int MaxEmbeddingLength { get; set; } = 512;

    /// <summary>
    /// Gets the key composition settings used when building similarity payloads.
    /// </summary>
    public RequestCacheKeyOptions KeyOptions { get; } = new();
}
