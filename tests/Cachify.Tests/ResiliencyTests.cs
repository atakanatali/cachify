using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cachify.Abstractions;
using Cachify.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cachify.Tests;

public sealed class ResiliencyTests
{
    [Fact]
    public async Task GetAsync_returns_stale_within_fail_safe_window()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateComposite(timeProvider, out var memory, resilience =>
        {
            resilience.FailSafeMaxDuration = TimeSpan.FromSeconds(5);
        });
        _ = memory;

        await cache.SetAsync("user:1", "cached").ConfigureAwait(false);

        timeProvider.Advance(TimeSpan.FromSeconds(11));

        var value = await cache.GetAsync<string>("user:1").ConfigureAwait(false);
        value.Should().Be("cached");
    }

    [Fact]
    public async Task GetAsync_returns_null_after_fail_safe_window()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateComposite(timeProvider, out var memory, resilience =>
        {
            resilience.FailSafeMaxDuration = TimeSpan.FromSeconds(5);
        });
        _ = memory;

        await cache.SetAsync("user:2", "cached").ConfigureAwait(false);

        timeProvider.Advance(TimeSpan.FromSeconds(16));

        var value = await cache.GetAsync<string>("user:2").ConfigureAwait(false);
        value.Should().BeNull();
    }

    [Fact]
    public async Task Soft_timeout_returns_stale_and_refreshes_in_background()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateComposite(timeProvider, out var memory, resilience =>
        {
            resilience.FailSafeMaxDuration = TimeSpan.FromSeconds(5);
            resilience.SoftTimeout = TimeSpan.FromSeconds(2);
            resilience.HardTimeout = TimeSpan.FromSeconds(10);
            resilience.EnableBackgroundRefresh = true;
        });

        var setSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        memory.OnSet = (_, value) =>
        {
            if (value is string text && text == "fresh")
            {
                setSignal.TrySetResult(text);
            }
        };

        await cache.SetAsync("user:3", "stale").ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromSeconds(11));

        var factorySignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchTask = cache.GetOrSetAsync("user:3", _ => factorySignal.Task);

        timeProvider.Advance(TimeSpan.FromSeconds(3));
        var value = await fetchTask.ConfigureAwait(false);

        value.Should().Be("stale");

        factorySignal.TrySetResult("fresh");
        await setSignal.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        var refreshed = await cache.GetAsync<string>("user:3").ConfigureAwait(false);
        refreshed.Should().Be("fresh");
    }

    [Fact]
    public async Task Hard_timeout_throws_when_no_stale_exists()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateComposite(timeProvider, out var memory, resilience =>
        {
            resilience.FailSafeMaxDuration = TimeSpan.Zero;
            resilience.HardTimeout = TimeSpan.FromSeconds(2);
        });

        var factorySignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchTask = cache.GetOrSetAsync("user:4", _ => factorySignal.Task);

        timeProvider.Advance(TimeSpan.FromSeconds(3));

        await FluentActions.Awaiting(() => fetchTask)
            .Should()
            .ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Concurrent_calls_share_single_factory_execution()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = CreateComposite(timeProvider, out var memory, resilience =>
        {
            resilience.FailSafeMaxDuration = TimeSpan.Zero;
        });

        var factorySignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        Task<string> Factory(CancellationToken _) 
        {
            Interlocked.Increment(ref callCount);
            return factorySignal.Task;
        }

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => cache.GetOrSetAsync("user:5", Factory))
            .ToArray();

        callCount.Should().Be(1);

        factorySignal.TrySetResult("value");
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        results.Should().AllBeEquivalentTo("value");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task L2_failure_returns_stale_from_l1()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var memory = new TestCacheService();
        var distributed = new TestCacheService { ThrowOnGet = true };
        var cache = CreateComposite(timeProvider, memory, distributed, resilience =>
        {
            resilience.FailSafeMaxDuration = TimeSpan.FromSeconds(5);
        });

        await cache.SetAsync("user:6", "cached").ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromSeconds(11));

        var value = await cache.GetAsync<string>("user:6").ConfigureAwait(false);
        value.Should().Be("cached");
    }

    private static CompositeCacheService CreateComposite(
        ManualTimeProvider timeProvider,
        out TestCacheService memory,
        Action<CacheResilienceOptions> configureResilience)
    {
        memory = new TestCacheService();
        return CreateComposite(timeProvider, memory, null, configureResilience);
    }

    private static CompositeCacheService CreateComposite(
        ManualTimeProvider timeProvider,
        TestCacheService? memory,
        TestCacheService? distributed,
        Action<CacheResilienceOptions> configureResilience)
    {
        var resilience = new CacheResilienceOptions();
        configureResilience(resilience);

        var options = new CachifyOptions
        {
            DefaultTtl = TimeSpan.FromSeconds(10),
            Resilience = resilience,
            TimeProvider = timeProvider
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        return new CompositeCacheService(
            memory,
            distributed,
            options,
            new DefaultCacheKeyBuilder(),
            new CacheStampedeGuard(),
            loggerFactory.CreateLogger<CompositeCacheService>());
    }
}
