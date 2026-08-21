using Basin.Scene;

namespace Basin.Effects;

public struct EffectTimeline
{
    private long _startNanos;
    private long _durationNanos;
    private bool _started;
    private bool _anchorPending;
    private bool _finalTickSeen;

    public EasingCurve Easing { get; set; }

    public bool IsStarted => _started;

    public void Start(in FrameTick now, long durationNanos)
    {
        _startNanos = now.TargetPresentNanos;
        _durationNanos = Math.Max(1, durationNanos);
        _started = true;
        _anchorPending = false;
        _finalTickSeen = false;
    }

    public void Start(long durationNanos)
    {
        _startNanos = 0;
        _durationNanos = Math.Max(1, durationNanos);
        _started = true;
        _anchorPending = true;
        _finalTickSeen = false;
    }

    public void Anchor(in FrameTick now)
    {
        if (_started && _anchorPending)
        {
            _startNanos = now.TargetPresentNanos;
            _anchorPending = false;
        }
    }

    private void AutoAnchor(in FrameTick now)
    {
        if (_started && _anchorPending)
        {
            _startNanos = now.TargetPresentNanos - Math.Max(0, now.RefreshIntervalNanos);
            _anchorPending = false;
        }
    }

    public void RestartPreservingProgress(in FrameTick now)
    {
        if (!_started)
        {
            Start(now, _durationNanos);
            return;
        }

        AutoAnchor(now);
        var raw = RawProgress(now);
        _startNanos = now.TargetPresentNanos - (long)((1.0 - raw) * _durationNanos);
        _finalTickSeen = false;
    }

    public double Progress(in FrameTick now)
    {
        AutoAnchor(now);
        return Easing.Apply(RawProgress(now));
    }

    public bool Running(in FrameTick now)
    {
        if (!_started)
        {
            return false;
        }

        AutoAnchor(now);

        if (RawProgress(now) < 1.0)
        {
            return true;
        }

        if (_finalTickSeen)
        {
            return false;
        }

        _finalTickSeen = true;
        return true;
    }

    private readonly double RawProgress(in FrameTick now) => !_started
        ? 0
        : Math.Clamp((now.TargetPresentNanos - _startNanos) / (double)_durationNanos, 0, 1);
}
