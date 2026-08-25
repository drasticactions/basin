using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

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

    public ShakeCursorEffect(ShakeCursorOptions options = default) =>
        _options = options == default ? new ShakeCursorOptions() : options;

    public ShakeCursorOptions Options => _options;

    public double Magnification => _current;

    public double TargetMagnification => _target;

    public bool IsActive => _current != 1.0 || _animating;

    public void Shake(in FrameTick now)
    {
        _thread.Assert();
        var magnification = _target == 1.0
            ? _options.Magnification
            : _target + _options.OverMagnification;
        AnimateTo(magnification, now);
        _deflateAtNanos = now.TargetPresentNanos + (long)(_options.DeflateAfterMillis * 1_000_000);
    }

    public void Settle(in FrameTick now)
    {
        _thread.Assert();
        AnimateTo(1.0, now);
        _deflateAtNanos = 0;
    }

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
