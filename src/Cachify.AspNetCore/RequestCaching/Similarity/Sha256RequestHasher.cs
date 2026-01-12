using System.Security.Cryptography;
using System.Text;

namespace Cachify.AspNetCore;

/// <summary>
/// Computes SHA-256 hashes for canonical request payloads.
/// </summary>
public sealed class Sha256RequestHasher : IRequestHasher
{
    /// <inheritdoc />
    public byte[] ComputeHash(string canonicalPayload)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
    }
}
