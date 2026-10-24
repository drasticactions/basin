using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs the shake cursor scale animation. It does not detect shakes or draw the cursor. The consumer reports shakes and reads <see cref="Magnification"/>.
/// </summary>
public sealed class ShakeCursorEffect
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly ShakeCursorOptions _options;
    private EffectTimeline _timeline;
    private double _current = 1.0;
    private double _startedAt = 1.0;
    private double _target = 1.0;
    private long _deflateAtNanos;
    private bool _animating;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShakeCursorEffect"/> class.
    /// </summary>
    /// <param name="options">The settings. The default value uses the default <see cref="ShakeCursorOptions"/>.</param>
    public ShakeCursorEffect(ShakeCursorOptions options = default) =>
        _options = options == default ? new ShakeCursorOptions() : options;

    /// <summary>
    /// Gets the settings of the effect.
    /// </summary>
    public ShakeCursorOptions Options => _options;

    /// <summary>
    /// Gets the current cursor scale. It is 1 at rest.
    /// </summary>
    public double Magnification => _current;

    /// <summary>
    /// Gets the scale that the current animation moves toward.
    /// </summary>
    public double TargetMagnification => _target;

    /// <summary>
    /// Gets a value indicating whether the cursor is enlarged or its scale still changes.
    /// </summary>
    public bool IsActive => _current != 1.0 || _animating;

    /// <summary>
    /// Reports a shake. The first one grows the cursor to <see cref="ShakeCursorOptions.Magnification"/>, and each further one adds <see cref="ShakeCursorOptions.OverMagnification"/>. It also restarts the deflate timer.
    /// </summary>
    /// <param name="now">The tick of the shake.</param>
    public void Shake(in FrameTick now)
    {
        _thread.Assert();
        var magnification = _target == 1.0
            ? _options.Magnification
            : _target + _options.OverMagnification;
        AnimateTo(magnification, now);
        _deflateAtNanos = now.TargetPresentNanos + (long)(_options.DeflateAfterMillis * 1_000_000);
    }

    /// <summary>
    /// Shrinks the cursor back to its normal scale and stops the deflate timer.
    /// </summary>
    /// <param name="now">The tick that the shrink starts at.</param>
    public void Settle(in FrameTick now)
    {
        _thread.Assert();
        AnimateTo(1.0, now);
        _deflateAtNanos = 0;
    }

    /// <summary>
    /// Updates the scale for a tick, and starts the shrink when the deflate time has come.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns>The value of <see cref="IsActive"/> after the step.</returns>
    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (_deflateAtNanos != 0 && tick.TargetPresentNanos >= _deflateAtNanos)
        {
            Settle(tick);
        }

        if (_animating)
        {
            var progress = _timeline.Progress(tick);
            _current = _startedAt + ((_target - _startedAt) * progress);
            if (!_timeline.Running(tick))
            {
                _animating = false;
                _current = _target;
            }
        }

        return IsActive;
    }

    private void AnimateTo(double magnification, in FrameTick now)
    {
        if (Math.Abs(_target - magnification) < 1e-9)
        {
            return;
        }

        _startedAt = _current;
        _target = magnification;
        _timeline.Easing = EasingCurve.InOutCubic;
        _timeline.Start(now, (long)(_options.RampMillis * 1_000_000));
        _animating = true;
    }
}
