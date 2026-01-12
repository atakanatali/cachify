namespace Cachify.Abstractions;

/// <summary>
/// Provides serialization services for cached payloads.
/// </summary>
public interface ICacheSerializer
{
    /// <summary>
    /// Serializes a value into a byte array for storage.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes a value from a byte array.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="payload">The payload bytes.</param>
    T? Deserialize<T>(byte[] payload);
}
