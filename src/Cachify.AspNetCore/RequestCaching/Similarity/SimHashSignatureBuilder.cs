namespace Cachify.AspNetCore;

/// <summary>
/// Builds compact SimHash signatures from canonical payloads.
/// </summary>
public sealed class SimHashSignatureBuilder
{
    /// <summary>
    /// Builds a SimHash signature for the provided payload.
    /// </summary>
    /// <param name="canonicalPayload">The canonical payload.</param>
    /// <param name="maxTokens">The maximum number of tokens to process.</param>
    /// <returns>The signature and token count.</returns>
    public (ulong Signature, int TokenCount) BuildSignature(string canonicalPayload, int maxTokens)
    {
        Span<int> weights = stackalloc int[64];
        var tokenCount = 0;
        var tokenBuffer = new char[64];
        var tokenLength = 0;

        void ProcessToken()
        {
            if (tokenLength == 0 || tokenCount >= maxTokens)
            {
                tokenLength = 0;
                return;
            }

            var hash = HashToken(tokenBuffer.AsSpan(0, tokenLength));
            for (var bit = 0; bit < 64; bit++)
            {
                if (((hash >> bit) & 1UL) == 1UL)
                {
                    weights[bit]++;
                }
                else
                {
                    weights[bit]--;
                }
            }

            tokenCount++;
            tokenLength = 0;
        }

        foreach (var character in canonicalPayload)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (tokenLength == tokenBuffer.Length)
                {
                    ProcessToken();
                }

                tokenBuffer[tokenLength++] = char.ToLowerInvariant(character);
            }
            else
            {
                ProcessToken();
                if (tokenCount >= maxTokens)
                {
                    break;
                }
            }
        }

        ProcessToken();

        ulong signature = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if (weights[bit] >= 0)
            {
                signature |= 1UL << bit;
            }
        }

        return (signature, tokenCount);
    }

    /// <summary>
    /// Computes a FNV-1a hash for a token span.
    /// </summary>
    /// <param name="token">The token to hash.</param>
    /// <returns>The 64-bit token hash.</returns>
    private static ulong HashToken(ReadOnlySpan<char> token)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;

        foreach (var character in token)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }
}
