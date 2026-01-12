namespace Cachify.Memcached;

/// <summary>
/// Configures Memcached connectivity for Cachify.
/// </summary>
public sealed class MemcachedOptions
{
    /// <summary>
    /// Gets or sets the Memcached connection string.
    /// </summary>
    public string? ConnectionString { get; set; }
}
