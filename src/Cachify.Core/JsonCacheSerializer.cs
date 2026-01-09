using System.Text.Json;
using Cachify.Abstractions;

namespace Cachify.Core;

public sealed class JsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;

    public JsonCacheSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, _options);
    }

    public T? Deserialize<T>(byte[] payload)
    {
        return JsonSerializer.Deserialize<T>(payload, _options);
    }
}
