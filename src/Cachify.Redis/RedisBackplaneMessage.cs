using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cachify.Abstractions;

namespace Cachify.Redis;

/// <summary>
/// Represents the serialized backplane payload for Redis pub/sub.
/// </summary>
/// <remarks>
/// Design Notes: this payload includes a version field to support forward compatibility and
/// avoids additional framing by staying within JSON primitives.
/// </remarks>
internal sealed class RedisBackplaneMessage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Gets the current message format version.
    /// </summary>
    internal const int CurrentVersion = 1;

    /// <summary>
    /// Gets the format version.
    /// </summary>
    [JsonPropertyName("v")]
    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// Gets the source instance identifier.
    /// </summary>
    [JsonPropertyName("src")]
    public string SourceId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the cache key, when publishing a single invalidation.
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>
    /// Gets the cache tag, when publishing a single invalidation.
    /// </summary>
    [JsonPropertyName("tag")]
    public string? Tag { get; init; }

    /// <summary>
    /// Gets the batched invalidations, when publishing multiple entries at once.
    /// </summary>
    [JsonPropertyName("items")]
    public List<RedisBackplaneInvalidation>? Invalidations { get; init; }

    /// <summary>
    /// Creates a message for a single invalidation.
    /// </summary>
    internal static RedisBackplaneMessage CreateSingle(CacheInvalidation invalidation)
    {
        return new RedisBackplaneMessage
        {
            SourceId = invalidation.SourceId,
            Key = invalidation.Key,
            Tag = invalidation.Tag
        };
    }

    /// <summary>
    /// Creates a message for a batched set of invalidations.
    /// </summary>
    internal static RedisBackplaneMessage CreateBatch(string sourceId, IReadOnlyCollection<CacheInvalidation> invalidations)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentNullException(nameof(sourceId));
        }

        if (invalidations is null)
        {
            throw new ArgumentNullException(nameof(invalidations));
        }

        return new RedisBackplaneMessage
        {
            SourceId = sourceId,
            Invalidations = invalidations
                .Select(invalidation => new RedisBackplaneInvalidation
                {
                    Key = invalidation.Key,
                    Tag = invalidation.Tag
                })
                .ToList()
        };
    }

    /// <summary>
    /// Serializes the message to JSON.
    /// </summary>
    internal string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Attempts to deserialize a backplane message from JSON.
    /// </summary>
    internal static bool TryDeserialize(string payload, out RedisBackplaneMessage message)
    {
        message = null!;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RedisBackplaneMessage>(payload, SerializerOptions);
            if (parsed is null)
            {
                return false;
            }

            if (parsed.Version != CurrentVersion)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(parsed.SourceId))
            {
                return false;
            }

            if (parsed.Invalidations is { Count: > 0 } || !string.IsNullOrWhiteSpace(parsed.Key) || !string.IsNullOrWhiteSpace(parsed.Tag))
            {
                message = parsed;
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Expands the message into individual invalidation events.
    /// </summary>
    internal IEnumerable<CacheInvalidation> ToInvalidations()
    {
        if (Invalidations is { Count: > 0 })
        {
            foreach (var invalidation in Invalidations)
            {
                if (!string.IsNullOrWhiteSpace(invalidation.Key))
                {
                    yield return CacheInvalidation.ForKey(invalidation.Key, SourceId);
                }
                else if (!string.IsNullOrWhiteSpace(invalidation.Tag))
                {
                    yield return CacheInvalidation.ForTag(invalidation.Tag, SourceId);
                }
            }

            yield break;
        }

        if (!string.IsNullOrWhiteSpace(Key))
        {
            yield return CacheInvalidation.ForKey(Key, SourceId);
        }
        else if (!string.IsNullOrWhiteSpace(Tag))
        {
            yield return CacheInvalidation.ForTag(Tag, SourceId);
        }
    }

    /// <summary>
    /// Represents an invalidation entry within a batch payload.
    /// </summary>
    /// <remarks>
    /// Design Notes: the shape mirrors <see cref="CacheInvalidation"/> without the source identifier.
    /// </remarks>
    internal sealed class RedisBackplaneInvalidation
    {
        /// <summary>
        /// Gets or sets the cache key to invalidate.
        /// </summary>
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        /// <summary>
        /// Gets or sets the cache tag to invalidate.
        /// </summary>
        [JsonPropertyName("tag")]
        public string? Tag { get; init; }
    }
}
