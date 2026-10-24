using Basin.Kinematics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Moves one scalar of the canvas to a target, either on an eased timeline or on a critically damped spring.
/// </summary>
public sealed class CanvasMotion
{
    private EffectTimeline _timeline;
    private double _from;
    private double _to;
    private double _current;
    private bool _running;
    private SpringHandoff _spring;
    private bool _springing;

    /// <summary>
    /// Initializes a new instance of the <see cref="CanvasMotion"/> class with an ease-out Bézier.
    /// </summary>
    public CanvasMotion()
    {
        _timeline.Easing = EasingCurve.CubicBezier(0, 0, 0.2, 1);
    }

    /// <summary>
    /// Gets a value indicating whether a motion is running and <see cref="Step"/> must be called.
    /// </summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Gets the value from the last step, or the target when no motion runs.
    /// </summary>
    public double Current => _current;

    /// <summary>
    /// Gets the value that the motion moves to.
    /// </summary>
    public double Target => _to;

    /// <summary>
    /// Starts a timeline motion. A running motion continues from its current value and ignores <paramref name="from"/>.
    /// </summary>
    /// <param name="from">The start value when no motion runs.</param>
    /// <param name="to">The target value.</param>
    /// <param name="durationNanos">The duration in nanoseconds. Zero or less, or a start equal to the target, jumps to the target.</param>
    public void Begin(double from, double to, long durationNanos)
    {
        _springing = false;
        _from = _running ? _current : from;
        _current = _from;
        _to = to;
        if (durationNanos <= 0 || _from == _to)
        {
            _current = _to;
            _running = false;
            return;
        }

        _running = true;
        _timeline.Start(durationNanos);
    }

    /// <summary>
    /// Starts a spring from a release velocity. The value stays between the start and the target, so an overshoot never shows.
    /// </summary>
    /// <param name="from">The start value when no motion runs.</param>
    /// <param name="to">The target value.</param>
    /// <param name="velocity">The release velocity in value units a second.</param>
    /// <param name="stiffness">The spring stiffness.</param>
    /// <param name="epsilon">The distance at which the spring snaps to the target.</param>
    public void Settle(double from, double to, double velocity, double stiffness = 800, double epsilon = 0.001)
    {
        _from = _running ? _current : from;
        _current = _from;
        _to = to;
        _spring = new SpringHandoff(stiffness, 1.0) { Epsilon = epsilon };
        _spring.Start(_from, 0, velocity, 0, to, 0);
        _springing = _spring.IsMoving;
        _running = _springing;
        if (!_running)
        {
            _current = _to;
        }
    }

    /// <summary>
    /// Stops the motion where it is. <see cref="Current"/> keeps the last stepped value.
    /// </summary>
    public void Cancel() => _running = false;

    /// <summary>
    /// Advances the motion.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <param name="value">The new value, or the current value when no motion runs.</param>
    /// <returns><see langword="true"/> while the motion needs more frames.</returns>
    public bool Step(in FrameTick tick, out double value)
    {
        if (!_running)
        {
            value = _current;
            return false;
        }

        if (_springing)
        {
            var moving = _spring.Step(tick);
            var low = Math.Min(_from, _to);
            var high = Math.Max(_from, _to);
            _current = moving ? Math.Clamp(_spring.X, low, high) : _to;
            _running = moving;
            _springing = moving;
            value = _current;
            return moving;
        }

        var running = _timeline.Running(tick);
        var progress = _timeline.Progress(tick);
        _current = running ? _from + ((_to - _from) * progress) : _to;
        if (!running)
        {
            _running = false;
        }

        value = _current;
        return running;
    }
}
