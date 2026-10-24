using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Turns frame ticks into eased progress for one animation. It reads no wall clock, so a stepped clock gives deterministic frames.
/// </summary>
public struct EffectTimeline
{
    private long _startNanos;
    private long _durationNanos;
    private bool _started;
    private bool _anchorPending;
    private bool _finalTickSeen;

    /// <summary>
    /// Gets or sets the curve that <see cref="Progress"/> applies.
    /// </summary>
    public EasingCurve Easing { get; set; }

    /// <summary>
    /// Gets a value indicating whether a start method was called.
    /// </summary>
    public bool IsStarted => _started;

    /// <summary>
    /// Starts the timeline at the predicted present time of a tick.
    /// </summary>
    /// <param name="now">The tick that anchors the start.</param>
    /// <param name="durationNanos">The duration in nanoseconds. Values below 1 count as 1.</param>
    public void Start(in FrameTick now, long durationNanos)
    {
        _startNanos = now.TargetPresentNanos;
        _durationNanos = Math.Max(1, durationNanos);
        _started = true;
        _anchorPending = false;
        _finalTickSeen = false;
    }

    /// <summary>
    /// Starts the timeline without an anchor. The first read anchors it one refresh interval before that tick, so the first frame already moves.
    /// </summary>
    /// <param name="durationNanos">The duration in nanoseconds. Values below 1 count as 1.</param>
    public void Start(long durationNanos)
    {
        _startNanos = 0;
        _durationNanos = Math.Max(1, durationNanos);
        _started = true;
        _anchorPending = true;
        _finalTickSeen = false;
    }

    /// <summary>
    /// Anchors a pending start exactly at a tick. It does nothing when the start is already anchored.
    /// </summary>
    /// <param name="now">The tick that anchors the start.</param>
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

    /// <summary>
    /// Restarts the timeline so that its remaining time equals its elapsed time. A reversed animation then continues from its current position.
    /// </summary>
    /// <param name="now">The tick at which the reversal happens.</param>
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

    /// <summary>
    /// Returns the eased progress at a tick.
    /// </summary>
    /// <param name="now">The frame tick.</param>
    /// <returns>The eased value of the raw fraction from 0 to 1. It is the eased 0 before a start.</returns>
    public double Progress(in FrameTick now)
    {
        AutoAnchor(now);
        return Easing.Apply(RawProgress(now));
    }

    /// <summary>
    /// Returns whether the animation needs a frame at a tick. It returns <see langword="true"/> one extra tick after the end, so the final frame draws.
    /// </summary>
    /// <param name="now">The frame tick.</param>
    /// <returns><see langword="false"/> before a start and after the extra tick.</returns>
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
