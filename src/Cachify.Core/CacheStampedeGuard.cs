using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Cachify.Core;

public sealed class CacheStampedeGuard
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

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
