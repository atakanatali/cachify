using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cachify.Tests;

/// <summary>
/// A deterministic <see cref="TimeProvider"/> for unit tests with manual time advancement.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public ManualTimeProvider(DateTimeOffset? startUtc = null)
    {
        _utcNow = startUtc ?? DateTimeOffset.UtcNow;
        _timestamp = _utcNow.UtcTicks;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (_lock)
        {
            return _timestamp;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (_lock)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>
    /// Advances the current time and triggers any due timers.
    /// </summary>
    /// <param name="by">The amount of time to advance.</param>
    public void Advance(TimeSpan by)
    {
        List<(TimerCallback Callback, object? State)> callbacks = new();
        lock (_lock)
        {
            _utcNow = _utcNow.Add(by);
            _timestamp += by.Ticks;

            foreach (var timer in _timers.ToArray())
            {
                if (timer.TryFire(_utcNow, callbacks))
                {
                    if (timer.IsDisposed)
                    {
                        _timers.Remove(timer);
                    }
                }
            }
        }

        foreach (var callback in callbacks)
        {
            callback.Callback(callback.State);
        }
    }

    private sealed class ManualTimer : ITimer, IAsyncDisposable
    {
        private readonly ManualTimeProvider _provider;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset _dueAtUtc;
        private TimeSpan _period;
        private bool _disposed;

        public ManualTimer(ManualTimeProvider provider, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _provider = provider;
            _callback = callback;
            _state = state;
            _period = period;
            _dueAtUtc = CalculateDueTime(provider.GetUtcNow(), dueTime);
        }

        public bool IsDisposed => _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            lock (_provider._lock)
            {
                _period = period;
                _dueAtUtc = CalculateDueTime(_provider._utcNow, dueTime);
            }

            return true;
        }

        public void Dispose()
        {
            _disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public bool TryFire(DateTimeOffset nowUtc, List<(TimerCallback Callback, object? State)> callbacks)
        {
            if (_disposed || _dueAtUtc == DateTimeOffset.MaxValue || nowUtc < _dueAtUtc)
            {
                return false;
            }

            callbacks.Add((_callback, _state));

            if (_period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero)
            {
                _dueAtUtc = DateTimeOffset.MaxValue;
            }
            else
            {
                _dueAtUtc = _dueAtUtc.Add(_period);
                if (_dueAtUtc < nowUtc)
                {
                    _dueAtUtc = nowUtc.Add(_period);
                }
            }

            return true;
        }

        private static DateTimeOffset CalculateDueTime(DateTimeOffset nowUtc, TimeSpan dueTime)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                return DateTimeOffset.MaxValue;
            }

            return nowUtc.Add(dueTime);
        }
    }
}
