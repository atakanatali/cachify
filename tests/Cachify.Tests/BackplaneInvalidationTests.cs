using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Cachify.Abstractions;
using Cachify.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cachify.Tests;

public sealed class BackplaneInvalidationTests
{
    [Fact]
    public async Task Invalidation_Removes_L1_Entry_On_Other_Instance()
    {
        var backplane = new InMemoryBackplane();
        var memoryA = new TestCacheService();
        var memoryB = new TestCacheService();

        using var cacheA = CreateComposite(memoryA, backplane, "node-a");
        using var cacheB = CreateComposite(memoryB, backplane, "node-b");

        await cacheB.SetAsync("user:1", "stale");
        (await cacheB.GetAsync<string>("user:1")).Should().Be("stale");

        await cacheA.SetAsync("user:1", "fresh");

        var value = await cacheB.GetAsync<string>("user:1");
        value.Should().BeNull();
    }

    private static CompositeCacheService CreateComposite(
        TestCacheService memory,
        ICacheBackplane backplane,
        string instanceId)
    {
        var options = new CachifyOptions
        {
            Backplane = new CacheBackplaneOptions
            {
                Enabled = true,
                InstanceId = instanceId
            }
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        return new CompositeCacheService(
            memory,
            null,
            backplane,
            options,
            new DefaultCacheKeyBuilder(),
            new CacheStampedeGuard(),
            loggerFactory.CreateLogger<CompositeCacheService>());
    }

    private sealed class InMemoryBackplane : ICacheBackplane
    {
        private readonly ConcurrentDictionary<Guid, Func<CacheInvalidation, CancellationToken, Task>> _handlers = new();

        public Task PublishInvalidationAsync(CacheInvalidation invalidation, CancellationToken cancellationToken = default)
        {
            if (_handlers.IsEmpty)
            {
                return Task.CompletedTask;
            }

            var tasks = new Task[_handlers.Count];
            var index = 0;
            foreach (var handler in _handlers.Values)
            {
                tasks[index++] = handler(invalidation, cancellationToken);
            }

            return Task.WhenAll(tasks);
        }

        public IDisposable Subscribe(Func<CacheInvalidation, CancellationToken, Task> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var id = Guid.NewGuid();
            _handlers[id] = handler;
            return new BackplaneSubscription(() => _handlers.TryRemove(id, out _));
        }

        private sealed class BackplaneSubscription : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;

            public BackplaneSubscription(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _onDispose();
            }
        }
    }
}
