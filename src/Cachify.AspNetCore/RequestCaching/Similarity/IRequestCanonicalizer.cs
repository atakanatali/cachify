namespace Cachify.AspNetCore;

/// <summary>
/// Converts request payloads into canonical representations suitable for hashing and similarity scoring.
/// </summary>
public interface IRequestCanonicalizer
{
    /// <summary>
    /// Produces a canonical representation of the provided payload.
    /// </summary>
    /// <param name="payload">The raw request payload.</param>
    /// <param name="contentType">The payload content type, if available.</param>
    /// <returns>The canonical payload, or <c>null</c> when canonicalization fails.</returns>
    string? Canonicalize(string payload, string? contentType);
}
