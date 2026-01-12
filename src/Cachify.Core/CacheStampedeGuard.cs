using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Cachify.Core;

/// <summary>
/// Provides per-key asynchronous locking to prevent cache stampedes.
/// </summary>
public sealed class CacheStampedeGuard
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>
    /// Runs the factory while holding a per-key lock to avoid duplicate work.
    /// </summary>
    /// <typeparam name="T">The factory result type.</typeparam>
    /// <param name="key">The key used to scope the lock.</param>
    /// <param name="factory">The work to execute.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    public async Task<T> RunAsync<T>(string key, Func<Task<T>> factory, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await factory().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                _locks.TryRemove(key, out _);
            }
        }
    }
}
