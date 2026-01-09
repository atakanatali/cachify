namespace Cachify.Abstractions;

public interface ICacheSerializer
{
    byte[] Serialize<T>(T value);

    T? Deserialize<T>(byte[] payload);
}
