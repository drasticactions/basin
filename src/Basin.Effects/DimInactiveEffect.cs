using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class DimInactiveEffect
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IPixelShader? _shader;
    private readonly DimInactiveOptions _options;
    private readonly double _strength;
    private EffectTimeline _timeline;
    private double _from;
    private double _to;
    private double _factor;
    private bool _animating;

    public DimInactiveEffect(IPixelShader? shader, DimInactiveOptions options = default)
    {
        _shader = shader;
        _options = options == default ? new DimInactiveOptions() : options;
        _strength = Math.Clamp(_options.Strength / 100.0, 0.1, 0.9);
    }

    public DimInactiveOptions Options => _options;

    public double Strength => _strength;

    public IPixelShader? Shader => _shader;

    public double Factor => _factor;

    public double Dim => 1.0 - (_strength * _factor);

    public bool IsAnimating => _animating;

    public void FadeTo(double factor, in FrameTick now, AnimationDuration duration)
    {
        _thread.Assert();
        var target = Math.Clamp(factor, 0, 1);
        if (Math.Abs(target - _to) < 1e-9)
        {
            return;
        }

        _to = target;
        if (duration.IsDisabled)
        {
            _factor = target;
            _animating = false;
            Push();
            return;
        }

        _from = _factor;
        _timeline.Easing = EasingCurve.Linear;
        _timeline.Start(now, duration.Nanos);
        _animating = true;
    }

    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (!_animating)
        {
            return false;
        }

        var progress = _timeline.Progress(tick);
        _factor = _from + ((_to - _from) * progress);
        Push();
        if (_timeline.Running(tick))
        {
            return true;
        }

        _factor = _to;
        Push();
        _animating = false;
        return false;
    }

    private void Push() => _shader?.SetUniforms([(float)Dim]);
}
