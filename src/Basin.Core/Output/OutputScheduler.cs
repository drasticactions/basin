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
    private bool _pacedFromLastFrame;
    private long _commits;
    private bool _disposed;
    private bool _idleRaised = true;

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

    public event Action? Idle;

    public bool IsIdle => !_repaintQueued && !_commitInFlight && !_timerArmed && !_idleQueued && !_firing;

    public long MissedVblanks { get; private set; }

    public int RenderAheadMillis { get; set; } = 2;

    public bool ArrivalMode { get; set; }

    public bool MeasuresRenderTime => _renderSamples > 0;

    public long RenderLeadNanos => BaseLeadNanos();

    public void NotifyRenderTime(long nanos)
    {
        if (nanos <= 0)
        {
            return;
        }

        _renderTimes[_renderNext] = nanos;
        _renderNext = (_renderNext + 1) % _renderTimes.Length;
        if (_renderSamples < _renderTimes.Length)
        {
            _renderSamples++;
        }

        var max = 0L;
        for (var i = 0; i < _renderSamples; i++)
        {
            max = Math.Max(max, _renderTimes[i]);
        }

        _renderMax = max;
    }

    private readonly long[] _renderTimes = new long[120];
    private int _renderNext;
    private int _renderSamples;
    private long _renderMax;

    private const long UnmeasuredLeadNanos = 7_000_000;

    private const long MeasuredMarginNanos = 1_500_000;

    public void ScheduleRepaint()
    {
        if (_disposed || _repaintQueued)
        {
            return;
        }

        _repaintQueued = true;
        _idleRaised = false;
        if (_firing)
        {
            return;
        }

        if (_commitInFlight || _timerArmed || _idleQueued)
        {
            Log.Debug($"scheduler: repaint queued (inFlight={_commitInFlight} timer={_timerArmed})");
            return;
        }

        if (ArrivalMode)
        {
            ArmArrival();
            return;
        }

        if (TryArmDeadline())
        {
            return;
        }

        var elapsed = Environment.TickCount64 - _lastFireTick;
        var interval = IntervalMs();
        if (elapsed < interval)
        {
            _timerArmed = true;
            _timer.UpdateTimer((int)Math.Max(1, (long)Math.Ceiling(interval - elapsed)));
            return;
        }

        QueueIdle();
    }

    public long PredictedVblankNanos
    {
        get
        {
            var interval = IntervalNanos();
            if (ArrivalMode)
            {
                return _lastFrameNanos == 0 ? MonotonicClock.Nanos : Math.Max(MonotonicClock.Nanos, _lastFrameNanos + interval);
            }

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

    private long IntervalNanos()
    {
        var interval = _output.CurrentMode.RefreshIntervalNanoseconds;
        return interval > 0 ? interval : 16_000_000L;
    }

    private double IntervalMs()
    {
        var refreshMilliHz = _output.CurrentMode.RefreshMilliHz;
        return refreshMilliHz > 0 ? 1_000_000.0 / refreshMilliHz : 16.0;
    }

    public void NotifyCommitted()
    {
        _commitInFlight = true;
        _commits++;
    }

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
        var intervalNanos = IntervalNanos();
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

    private bool TryArmDeadlineAfter(long vblank)
    {
        var intervalNanos = IntervalNanos();
        if (vblank == 0 || intervalNanos <= 0)
        {
            return false;
        }

        var now = MonotonicClock.Nanos;
        var lead = RenderAheadNanos(intervalNanos);
        var next = vblank + intervalNanos;
        while (next - lead - now < 1_000_000)
        {
            next += intervalNanos;
        }

        _timerArmed = true;
        _timer.UpdateTimer((int)Math.Max(1, (next - lead - now) / 1_000_000));
        return true;
    }

    private void ArmArrival() => QueueIdle();

    private long BaseLeadNanos()
    {
        var floor = RenderAheadMillis * 1_000_000L;
        return _renderSamples > 0 ? Math.Max(floor, _renderMax + MeasuredMarginNanos) : Math.Max(floor, UnmeasuredLeadNanos);
    }

    private long RenderAheadNanos(long intervalNanos)
    {
        var lead = BaseLeadNanos() + Math.Min(_leadBoostNanos, intervalNanos);
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
            RaiseIfIdle();
            return;
        }

        _repaintQueued = false;
        _lastFireTick = Environment.TickCount64;
        _lastFireNanos = MonotonicClock.Nanos;
        var target = PredictedVblankNanos;
        var commits = _commits;
        _firing = true;
        AllocationScope.Pause();
        try
        {
            Repaint?.Invoke();
        }
        finally
        {
            AllocationScope.Resume();
            _firing = false;
        }

        if (_repaintQueued && !_commitInFlight && !_disposed && !_timerArmed && !_idleQueued && ArrivalMode)
        {
            ArmArrival();
        }
        else if (_repaintQueued && !_commitInFlight && !_disposed && !_timerArmed && !_idleQueued &&
            !(_commits == commits ? TryArmDeadlineAfter(target) : TryArmDeadline()))
        {
            var elapsed = Environment.TickCount64 - _lastFireTick;
            var interval = IntervalMs();
            _timerArmed = true;
            _timer.UpdateTimer((int)Math.Max(1, (long)Math.Ceiling(interval - elapsed)));
        }

        RaiseIfIdle();
    }

    private void RaiseIfIdle()
    {
        if (_disposed)
        {
            return;
        }

        if (!IsIdle)
        {
            _idleRaised = false;
            return;
        }

        if (!_idleRaised)
        {
            _idleRaised = true;
            Idle?.Invoke();
        }
    }

    private void OnFrame()
    {
        var now = MonotonicClock.Nanos;
        var interval = IntervalNanos();
        var previousFrame = _lastFrameNanos;
        _lastFrameNanos = _presentedNanos > 0 && now - _presentedNanos < interval * 2 ? _presentedNanos : now;

        if (_commitInFlight && _lastFireNanos > 0 && interval > 0)
        {
            var late = _lastFrameNanos - _lastFireNanos > interval + (interval >> 2);
            var skipped = _pacedFromLastFrame && previousFrame > 0 &&
                _lastFrameNanos - previousFrame > interval + (interval >> 1);
            if (late || skipped)
            {
                _leadBoostNanos = Math.Min(Math.Min(_leadBoostNanos, interval) + 3_000_000, interval);
                _onTimeStreak = 0;
                _everMissed = true;
                MissedVblanks++;
                Log.Debug($"scheduler: missed vblank; lead boost {_leadBoostNanos / 1_000_000} ms");
            }
            else if (_leadBoostNanos > 0 && ++_onTimeStreak >= (_everMissed ? 600 : 120))
            {
                _onTimeStreak = 0;
                _leadBoostNanos = Math.Max(0, Math.Min(_leadBoostNanos, interval) - 500_000);
            }
        }

        _commitInFlight = false;
        _pacedFromLastFrame = _repaintQueued;
        if (_repaintQueued && !_disposed && !_timerArmed && !_idleQueued && ArrivalMode)
        {
            ArmArrival();
        }
        else if (_repaintQueued && !_disposed && !_timerArmed && !_idleQueued)
        {
            Log.Debug($"scheduler: flip; deadline armed");
            if (!TryArmDeadline())
            {
                QueueIdle();
            }
        }

        RaiseIfIdle();
    }

    private int _scopedFires;

    private void OnTimer()
    {
        Log.Debug($"scheduler: timer fired (queued={_repaintQueued})");
        _timerArmed = false;
        if (_scopedFires < 10)
        {
            _scopedFires++;
            Fire();
            return;
        }

        AllocationScope.Begin(region: "SchedulerTimer", forgiving: true);
        try
        {
            Fire();
        }
        finally
        {
            AllocationScope.End();
        }
    }
}
