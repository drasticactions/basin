using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Fades the dim factor of an inactive window through a pixel shader. The consumer decides which windows dim and attaches the shader to them.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="DimInactiveEffect"/> class.
    /// </summary>
    /// <param name="shader">The compiled <see cref="DimShader"/>, or <see langword="null"/> when the renderer cannot compile it.</param>
    /// <param name="options">The settings. The default value means <see cref="DimInactiveOptions()"/>.</param>
    public DimInactiveEffect(IPixelShader? shader, DimInactiveOptions options = default)
    {
        _shader = shader;
        _options = options == default ? new DimInactiveOptions() : options;
        _strength = Math.Clamp(_options.Strength / 100.0, 0.1, 0.9);
    }

    /// <summary>
    /// Gets the settings that the effect was created with.
    /// </summary>
    public DimInactiveOptions Options => _options;

    /// <summary>
    /// Gets the strength as a fraction, clamped to the range from 0.1 to 0.9.
    /// </summary>
    public double Strength => _strength;

    /// <summary>
    /// Gets the shader that the effect drives, or <see langword="null"/> when there is none.
    /// </summary>
    public IPixelShader? Shader => _shader;

    /// <summary>
    /// Gets how far the dim has faded in, from 0 for none to 1 for full.
    /// </summary>
    public double Factor => _factor;

    /// <summary>
    /// Gets the brightness that the shader applies, from 1 down to 1 minus <see cref="Strength"/>.
    /// </summary>
    public double Dim => 1.0 - (_strength * _factor);

    /// <summary>
    /// Gets a value indicating whether a fade is running and <see cref="Step"/> must be called.
    /// </summary>
    public bool IsAnimating => _animating;

    /// <summary>
    /// Starts a linear fade from the current factor to a target. A target equal to the current target does nothing.
    /// </summary>
    /// <param name="factor">The target factor. The method clamps it to the range from 0 to 1.</param>
    /// <param name="now">The tick that anchors the fade.</param>
    /// <param name="duration">The fade duration. A disabled duration sets the factor at once.</param>
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

    /// <summary>
    /// Advances the fade and pushes the new brightness to the shader.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the fade needs more frames.</returns>
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
