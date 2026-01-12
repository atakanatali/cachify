using System.Text.Json;
using Cachify.Abstractions;

namespace Cachify.Core;

/// <summary>
/// Serializes cache payloads using <see cref="JsonSerializer"/> with web defaults.
/// </summary>
public sealed class JsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonCacheSerializer"/> class.
    /// </summary>
    /// <param name="options">Optional JSON serializer options.</param>
    public JsonCacheSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <inheritdoc />
    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, _options);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] payload)
    {
        return JsonSerializer.Deserialize<T>(payload, _options);
    }
}
