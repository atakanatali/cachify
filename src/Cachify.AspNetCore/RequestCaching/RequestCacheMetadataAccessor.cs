using Microsoft.AspNetCore.Http;

namespace Cachify.AspNetCore;

/// <summary>
/// Provides access to request cache metadata stored on the HTTP context.
/// </summary>
public static class RequestCacheMetadataAccessor
{
    /// <summary>
    /// The <see cref="HttpContext.Items"/> key used to store request cache metadata.
    /// </summary>
    public const string ItemKey = "Cachify.RequestCache.Metadata";

    /// <summary>
    /// Attempts to get request cache metadata from the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="metadata">The resolved cache metadata, when available.</param>
    /// <returns><c>true</c> if metadata was found; otherwise, <c>false</c>.</returns>
    public static bool TryGetMetadata(HttpContext context, out RequestCacheMetadata? metadata)
    {
        if (context.Items.TryGetValue(ItemKey, out var value) && value is RequestCacheMetadata cast)
        {
            metadata = cast;
            return true;
        }

        metadata = null;
        return false;
    }
}
