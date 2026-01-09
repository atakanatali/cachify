namespace Cachify.Abstractions;

public interface ICacheKeyBuilder
{
    string Build(string key, string? region = null, string? prefix = null);
}
