using Basin.Diagnostics;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

/// <summary>
/// Paces the repaints of one output against its flips. With no damage it arms no timer and does no work. It disposes itself when the output is destroyed.
/// </summary>
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
    private long _armedFireNanos;
    private long _fireLateNanos;
    private bool _pacedFromLastFrame;
    private long _commits;
    private bool _disposed;
    private bool _idleRaised = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputScheduler"/> class and subscribes to the frame, repaint and destroy events of the output.
    /// </summary>
    /// <param name="loop">The event loop that runs the timer and the idle callbacks.</param>
    /// <param name="output">The output to pace.</param>
    public OutputScheduler(ICompositorEventLoop loop, IOutput output)
    {
        _loop = loop;
        _output = output;
        _timer = loop.AddTimer(OnTimer);
        output.Frame += OnFrame;
        output.RepaintRequested += ScheduleRepaint;
        output.Destroyed += Dispose;
    }

    /// <summary>
    /// Occurs when the consumer must render and commit a frame. It always runs from an idle callback or the timer, never from inside <see cref="ScheduleRepaint"/>.
    /// </summary>
    public event Action? Repaint;

    /// <summary>
    /// Occurs once each time the scheduler goes from busy to idle. It does not repeat while the output stays idle.
    /// </summary>
    public event Action? Idle;

    /// <summary>
    /// Gets a value indicating whether no repaint is queued, armed, running or waiting for a flip.
    /// </summary>
    public bool IsIdle => !_repaintQueued && !_commitInFlight && !_timerArmed && !_idleQueued && !_firing;

    /// <summary>
    /// Gets the number of committed frames that missed their vblank since the scheduler started.
    /// </summary>
    public long MissedVblanks { get; private set; }

    /// <summary>
    /// Gets or sets the smallest render lead in milliseconds. The lead is the time before the predicted vblank at which a repaint fires.
    /// </summary>
    public int RenderAheadMillis { get; set; } = 2;

    /// <summary>
    /// Gets or sets a value indicating whether a repaint fires when damage arrives, not at a deadline. Use it for variable refresh and tearing, where the panel scans out on arrival.
    /// </summary>
    public bool ArrivalMode { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="NotifyRenderTime"/> gave at least one sample, so the lead comes from measured render times.
    /// </summary>
    public bool MeasuresRenderTime => _renderSamples > 0;

    /// <summary>
    /// Gets the render lead in nanoseconds without the boost that a missed vblank adds.
    /// </summary>
    public long RenderLeadNanos => BaseLeadNanos();

    /// <summary>
    /// Gets the render lead in nanoseconds that the next deadline uses, with the boost, clamped between 1 ms and one interval less 1 ms.
    /// </summary>
    public long EffectiveLeadNanos => RenderAheadNanos(IntervalNanos());

    /// <summary>
    /// Reports the time from the start of a repaint to the end of its GPU work. The lead becomes the largest of the last 120 reports plus 1.5 ms.
    /// </summary>
    /// <param name="nanos">The render time in nanoseconds. Zero or a negative value is ignored.</param>
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

    private const long LateFireNanos = 2_000_000;

    /// <summary>
    /// Asks for a repaint. Damage while idle repaints at the end of the current dispatch. While frames flow, the repaint fires at the deadline before the predicted vblank.
    /// </summary>
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

    /// <summary>
    /// Gets the monotonic time in nanoseconds of the next predicted vblank. It is zero before the first flip with fixed pacing.
    /// </summary>
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

    /// <summary>
    /// Tells the scheduler that the repaint committed a frame, so the next repaint waits for its flip.
    /// </summary>
    public void NotifyCommitted()
    {
        _commitInFlight = true;
        _commits++;
    }

    /// <summary>
    /// Gives the hardware timestamp of the last flip, which anchors the vblank prediction.
    /// </summary>
    /// <param name="presentedNanos">The flip time on the monotonic clock in nanoseconds. Zero or a negative value is ignored.</param>
    public void NotifyPresented(long presentedNanos)
    {
        if (presentedNanos > 0)
        {
            _presentedNanos = presentedNanos;
        }
    }

    /// <summary>
    /// Stops the timer and unsubscribes from the output. No <see cref="Repaint"/> fires after it.
    /// </summary>
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
            _armedFireNanos = now;
            QueueIdle();
            return true;
        }

        _timerArmed = true;
        _armedFireNanos = now + delayNanos;
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
        _armedFireNanos = next - lead;
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
        _fireLateNanos = _armedFireNanos > 0 ? _lastFireNanos - _armedFireNanos : 0;
        _armedFireNanos = 0;
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
            AllocationScope.Pause();
            try
            {
                Idle?.Invoke();
            }
            finally
            {
                AllocationScope.Resume();
            }
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
                MissedVblanks++;
                _onTimeStreak = 0;
                var flipNanos = _lastFrameNanos - _lastFireNanos;
                var gapNanos = skipped ? _lastFrameNanos - previousFrame : 0;
                if (_fireLateNanos > LateFireNanos)
                {
                    Log.Debug($"scheduler: missed vblank; the repaint fired {_fireLateNanos / 1_000_000} ms late, lead kept");
                }
                else if (flipNanos > interval * 3 || gapNanos > interval * 3)
                {
                    Log.Debug($"scheduler: missed vblank; the flip came {Math.Max(flipNanos, gapNanos) / 1_000_000} ms late, lead kept");
                }
                else
                {
                    _leadBoostNanos = Math.Min(Math.Min(_leadBoostNanos, interval) + 3_000_000, interval);
                    _everMissed = true;
                    Log.Debug($"scheduler: missed vblank; lead boost {_leadBoostNanos / 1_000_000} ms");
                }
            }
            else if (_leadBoostNanos > 0 && ++_onTimeStreak >= (_everMissed ? 600 : 120))
            {
                _onTimeStreak = 0;
                _leadBoostNanos = NarrowedBoost(interval);
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

    private long NarrowedBoost(long interval)
    {
        var boost = Math.Min(_leadBoostNanos, interval);
        if (_everMissed)
        {
            return Math.Max(0, boost - 500_000);
        }

        var half = boost / 2;
        return half > 500_000 ? half : 0;
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
