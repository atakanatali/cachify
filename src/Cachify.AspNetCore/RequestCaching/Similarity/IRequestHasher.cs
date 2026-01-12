namespace Cachify.AspNetCore;

/// <summary>
/// Computes stable hashes for canonical request payloads.
/// </summary>
public interface IRequestHasher
{
    /// <summary>
    /// Computes a hash for the provided canonical payload.
    /// </summary>
    /// <param name="canonicalPayload">The canonical payload.</param>
    /// <returns>The hash bytes.</returns>
    byte[] ComputeHash(string canonicalPayload);
}
