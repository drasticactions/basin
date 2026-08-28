using Basin.Diagnostics;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

public sealed class OutputScheduler : IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly IOutput _output;
    private readonly IEventSource _timer;
    private bool _timerArmed;
    private bool _commitInFlight;
    private bool _repaintQueued;
    private bool _idleQueued;
    private bool _firing;
    private long _lastFireTick;
    private long _lastFrameNanos;
    private long _presentedNanos;
    private long _lastFireNanos;
    private long _leadBoostNanos = long.MaxValue;
    private int _onTimeStreak;
    private bool _everMissed;
    private bool _disposed;

    public OutputScheduler(ICompositorEventLoop loop, IOutput output)
    {
        _loop = loop;
        _output = output;
        _timer = loop.AddTimer(OnTimer);
        output.Frame += OnFrame;
        output.RepaintRequested += ScheduleRepaint;
        output.Destroyed += Dispose;
    }

    public event Action? Repaint;

    public int RenderAheadMillis { get; set; } = 7;

    public void ScheduleRepaint()
    {
        if (_disposed || _repaintQueued)
        {
            return;
        }

        _repaintQueued = true;
        if (_firing)
        {
            return;
        }

        if (_commitInFlight || _timerArmed || _idleQueued)
        {
            Log.Debug($"scheduler: repaint queued (inFlight={_commitInFlight} timer={_timerArmed})");
            return;
        }

        if (TryArmDeadline())
        {
            return;
        }

        var elapsed = Environment.TickCount64 - _lastFireTick;
        var interval = (long)IntervalMs();
        if (elapsed < interval)
        {
            _timerArmed = true;
            _timer.UpdateTimer((int)Math.Max(1, interval - elapsed));
            return;
        }

        QueueIdle();
    }

    public long PredictedVblankNanos
    {
        get
        {
            var interval = (long)(IntervalMs() * 1_000_000);
            if (_lastFrameNanos == 0 || interval <= 0)
            {
                return 0;
            }

            var now = MonotonicClock.Nanos;
            if (now < _lastFrameNanos + interval)
            {
                return _lastFrameNanos + interval;
            }

            return _lastFrameNanos + (((now - _lastFrameNanos) / interval) + 1) * interval;
        }
    }

    private double IntervalMs()
    {
        var refreshMilliHz = _output.CurrentMode.RefreshMilliHz;
        return refreshMilliHz > 0 ? 1_000_000.0 / refreshMilliHz : 16.0;
    }

    public void NotifyCommitted() => _commitInFlight = true;

    public void NotifyPresented(long presentedNanos)
    {
        if (presentedNanos > 0)
        {
            _presentedNanos = presentedNanos;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _output.RepaintRequested -= ScheduleRepaint;
            _output.Frame -= OnFrame;
            _timer.Remove();
        }
    }

    private void QueueIdle()
    {
        if (!_idleQueued)
        {
            _idleQueued = true;
            _fireFromIdle ??= FireFromIdle;
            _loop.AddIdle(_fireFromIdle);
        }
    }

    private Action? _fireFromIdle;

    private bool TryArmDeadline()
    {
        var predicted = PredictedVblankNanos;
        if (predicted == 0)
        {
            return false;
        }

        var now = MonotonicClock.Nanos;
        var intervalNanos = (long)(IntervalMs() * 1_000_000);
        if (now - _lastFrameNanos > intervalNanos + (intervalNanos >> 1))
        {
            return false;
        }

        var delayNanos = predicted - RenderAheadNanos(intervalNanos) - now;
        if (delayNanos < 1_000_000)
        {
            QueueIdle();
            return true;
        }

        _timerArmed = true;
        _timer.UpdateTimer((int)(delayNanos / 1_000_000));
        return true;
    }

    private long RenderAheadNanos(long intervalNanos)
    {
        var lead = (RenderAheadMillis * 1_000_000L) + Math.Min(_leadBoostNanos, intervalNanos);
        var max = intervalNanos - 1_000_000;
        return Math.Max(1_000_000, Math.Min(lead, max));
    }

    private void FireFromIdle()
    {
        _idleQueued = false;
        Fire();
    }

    private void Fire()
    {
        if (_disposed || !_repaintQueued)
        {
            return;
        }

        _repaintQueued = false;
        _lastFireTick = Environment.TickCount64;
        _lastFireNanos = MonotonicClock.Nanos;
        _firing = true;
        try
        {
            Repaint?.Invoke();
        }
        finally
        {
            _firing = false;
        }

        if (_repaintQueued && !_commitInFlight && !_disposed && !_timerArmed && !_idleQueued && !TryArmDeadline())
        {
            var elapsed = Environment.TickCount64 - _lastFireTick;
            var interval = (long)IntervalMs();
            _timerArmed = true;
            _timer.UpdateTimer((int)Math.Max(1, interval - elapsed));
        }
    }

    private void OnFrame()
    {
        var now = MonotonicClock.Nanos;
        var interval = (long)(IntervalMs() * 1_000_000);
        var previousFrame = _lastFrameNanos;
        _lastFrameNanos = _presentedNanos > 0 && now - _presentedNanos < interval * 2 ? _presentedNanos : now;

        if (_commitInFlight && _lastFireNanos > 0 && interval > 0)
        {
            var late = _lastFrameNanos - _lastFireNanos > interval + (interval >> 2);
            var skipped = previousFrame > 0 && _lastFrameNanos - previousFrame > interval + (interval >> 1);
            if (late || skipped)
            {
                _leadBoostNanos = Math.Min(Math.Min(_leadBoostNanos, interval) + 3_000_000, interval);
                _onTimeStreak = 0;
                _everMissed = true;
                Log.Debug($"scheduler: missed vblank; lead boost {_leadBoostNanos / 1_000_000} ms");
            }
            else if (_leadBoostNanos > 0 && ++_onTimeStreak >= (_everMissed ? 600 : 120))
            {
                _onTimeStreak = 0;
                _leadBoostNanos = Math.Max(0, Math.Min(_leadBoostNanos, interval) - 500_000);
            }
        }

        _commitInFlight = false;
        if (_repaintQueued && !_disposed && !_timerArmed && !_idleQueued)
        {
            Log.Debug($"scheduler: flip; deadline armed");
            if (!TryArmDeadline())
            {
                QueueIdle();
            }
        }
    }

    private void OnTimer()
    {
        Log.Debug($"scheduler: timer fired (queued={_repaintQueued})");
        _timerArmed = false;
        Fire();
    }
}
