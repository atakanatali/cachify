using System;

namespace Cachify.Abstractions;

/// <summary>
/// Represents an invalidation event propagated through a backplane.
/// </summary>
/// <remarks>
/// Design Notes: invalidations carry the source instance to prevent echo handling and allow
/// transports to remain stateless.
/// </remarks>
public readonly record struct CacheInvalidation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheInvalidation"/> struct.
    /// </summary>
    /// <param name="key">The cache key to invalidate, if applicable.</param>
    /// <param name="tag">The cache tag to invalidate, if applicable.</param>
    /// <param name="sourceId">The identifier for the publishing instance.</param>
    /// <exception cref="ArgumentException">Thrown when both key and tag are empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceId"/> is null or whitespace.</exception>
    public CacheInvalidation(string? key, string? tag, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(tag))
        {
            throw new ArgumentException("An invalidation must specify a key or a tag.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentNullException(nameof(sourceId));
        }

        Key = key;
        Tag = tag;
        SourceId = sourceId;
    }

    /// <summary>
    /// Gets the cache key to invalidate, if provided.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// Gets the cache tag to invalidate, if provided.
    /// </summary>
    public string? Tag { get; }

    /// <summary>
    /// Gets the identifier for the publishing instance.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Creates an invalidation event for a specific key.
    /// </summary>
    /// <param name="key">The cache key to invalidate.</param>
    /// <param name="sourceId">The identifier for the publishing instance.</param>
    public static CacheInvalidation ForKey(string key, string sourceId) => new(key, null, sourceId);

    /// <summary>
    /// Creates an invalidation event for a specific tag.
    /// </summary>
    /// <param name="tag">The cache tag to invalidate.</param>
    /// <param name="sourceId">The identifier for the publishing instance.</param>
    public static CacheInvalidation ForTag(string tag, string sourceId) => new(null, tag, sourceId);
}
