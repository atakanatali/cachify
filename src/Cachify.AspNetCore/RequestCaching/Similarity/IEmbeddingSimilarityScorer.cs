namespace Cachify.AspNetCore;

/// <summary>
/// Provides optional embedding-based similarity scoring for request payloads.
/// </summary>
public interface IEmbeddingSimilarityScorer
{
    /// <summary>
    /// Builds an embedding vector for the provided canonical payload.
    /// </summary>
    /// <param name="canonicalPayload">The canonical request payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The embedding vector for the payload.</returns>
    ValueTask<ReadOnlyMemory<float>> CreateEmbeddingAsync(string canonicalPayload, CancellationToken cancellationToken);

    /// <summary>
    /// Calculates the similarity score between two embedding vectors.
    /// </summary>
    /// <param name="embeddingA">The first embedding vector.</param>
    /// <param name="embeddingB">The second embedding vector.</param>
    /// <returns>A similarity score between 0 and 1.</returns>
    double Score(ReadOnlyMemory<float> embeddingA, ReadOnlyMemory<float> embeddingB);
}
