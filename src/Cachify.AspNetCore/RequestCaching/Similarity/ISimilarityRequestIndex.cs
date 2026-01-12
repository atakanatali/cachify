namespace Cachify.AspNetCore;

/// <summary>
/// Stores compact similarity entries and retrieves candidate matches.
/// </summary>
public interface ISimilarityRequestIndex
{
    /// <summary>
    /// Retrieves a shortlist of indexed entries that may match the provided signature.
    /// </summary>
    /// <param name="signature">The request signature.</param>
    /// <param name="maxCandidates">The maximum number of candidates to return.</param>
    /// <returns>The candidate entries.</returns>
    IReadOnlyList<SimilarityIndexEntry> GetCandidates(ulong signature, int maxCandidates);

    /// <summary>
    /// Adds or updates a similarity entry in the index.
    /// </summary>
    /// <param name="entry">The entry to add.</param>
    void AddOrUpdate(SimilarityIndexEntry entry);

    /// <summary>
    /// Removes a similarity entry by cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key for the entry.</param>
    void Remove(string cacheKey);
}
