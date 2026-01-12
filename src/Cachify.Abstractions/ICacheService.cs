using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cachify.Abstractions;

/// <summary>
/// Defines the primary cache operations exposed by Cachify.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Gets a cached value by key.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a cached value by key.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="options">Optional entry options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached value by key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a cached value or invokes a factory to populate the cache.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The factory used to compute the value on a miss.</param>
    /// <param name="options">Optional entry options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options = null,
        CancellationToken cancellationToken = default);
}
