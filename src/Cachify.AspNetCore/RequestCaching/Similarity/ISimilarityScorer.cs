namespace Cachify.AspNetCore;

/// <summary>
/// Scores similarity between two requests based on their compact signatures.
/// </summary>
public interface ISimilarityScorer
{
    /// <summary>
    /// Computes a similarity score between two request signatures.
    /// </summary>
    /// <param name="left">The first request signature.</param>
    /// <param name="right">The second request signature.</param>
    /// <returns>A similarity score between 0 and 1.</returns>
    double Score(in SimilarityRequestFeatures left, in SimilarityRequestFeatures right);
}
