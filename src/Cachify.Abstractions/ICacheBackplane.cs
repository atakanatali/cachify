using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cachify.Abstractions;

/// <summary>
/// Provides a transport for distributed cache invalidation events.
/// </summary>
/// <remarks>
/// Design Notes: the backplane abstracts pub/sub transports while keeping the cache API
/// lightweight and optional for core users.
/// </remarks>
public interface ICacheBackplane
{
    /// <summary>
    /// Publishes a cache invalidation event to the backplane.
    /// </summary>
    /// <param name="invalidation">The invalidation event to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PublishInvalidationAsync(CacheInvalidation invalidation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to invalidation events from the backplane.
    /// </summary>
    /// <param name="handler">The handler invoked for each invalidation event.</param>
    /// <returns>An <see cref="IDisposable"/> that removes the subscription when disposed.</returns>
    IDisposable Subscribe(Func<CacheInvalidation, CancellationToken, Task> handler);
}
