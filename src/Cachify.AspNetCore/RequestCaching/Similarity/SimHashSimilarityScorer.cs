namespace Cachify.AspNetCore;

/// <summary>
/// Scores similarity by comparing 64-bit SimHash signatures.
/// </summary>
public sealed class SimHashSimilarityScorer : ISimilarityScorer
{
    /// <inheritdoc />
    public double Score(in SimilarityRequestFeatures left, in SimilarityRequestFeatures right)
    {
        if (left.TokenCount == 0 || right.TokenCount == 0)
        {
            return 0;
        }

        var distance = HammingDistance(left.Signature, right.Signature);
        return 1d - (distance / 64d);
    }

    /// <summary>
    /// Calculates the Hamming distance between two 64-bit signatures.
    /// </summary>
    /// <param name="left">The first signature.</param>
    /// <param name="right">The second signature.</param>
    /// <returns>The number of differing bits.</returns>
    private static int HammingDistance(ulong left, ulong right)
    {
        var value = left ^ right;
        var count = 0;
        while (value != 0)
        {
            count++;
            value &= value - 1;
        }

        return count;
    }
}
