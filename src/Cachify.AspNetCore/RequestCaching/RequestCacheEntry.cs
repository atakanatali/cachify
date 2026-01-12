namespace Cachify.AspNetCore;

/// <summary>
/// Represents a cached HTTP response payload.
/// </summary>
public sealed class RequestCacheEntry
{
    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the response body.
    /// </summary>
    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the response headers.
    /// </summary>
    public IDictionary<string, string[]> Headers { get; set; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the response content type.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the response was cached.
    /// </summary>
    public DateTimeOffset CachedAt { get; set; }

    /// <summary>
    /// Gets or sets the logical cache duration for the response.
    /// </summary>
    public TimeSpan Duration { get; set; }
}
