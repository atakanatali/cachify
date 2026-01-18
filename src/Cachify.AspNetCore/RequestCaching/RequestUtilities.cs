using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Cachify.AspNetCore;

internal static class RequestUtilities
{
    /// <summary>
    /// Computes a SHA-256 hash of the request body up to a configured size limit.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="maxBytes">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The hex-encoded hash or <c>null</c> when the body exceeds the limit.</returns>
    public static async Task<string?> ReadRequestBodyHashAsync(
        HttpRequest request,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (request.Body is null || !request.Body.CanRead)
        {
            return string.Empty;
        }

        request.EnableBuffering();
        request.Body.Position = 0;

        using var sha = SHA256.Create();
        var buffer = new byte[8192];
        long totalRead = 0;
        int read;

        while ((read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalRead += read;
            if (totalRead > maxBytes)
            {
                request.Body.Position = 0;
                return null;
            }

            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        request.Body.Position = 0;

        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
    }

    /// <summary>
    /// Reads the request body as a UTF-8 string up to a configured size limit.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="maxBytes">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request body string or <c>null</c> when the limit is exceeded.</returns>
    public static async Task<string?> ReadRequestBodyAsync(
        HttpRequest request,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (request.Body is null || !request.Body.CanRead)
        {
            return string.Empty;
        }

        request.EnableBuffering();
        request.Body.Position = 0;

        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        long totalRead = 0;

        try
        {
            using var stream = new MemoryStream();
            int read;
            while ((read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalRead += read;
                if (totalRead > maxBytes)
                {
                    request.Body.Position = 0;
                    return null;
                }

                stream.Write(buffer, 0, read);
            }

            request.Body.Position = 0;
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
