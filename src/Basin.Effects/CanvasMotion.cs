using Basin.Scene;

namespace Basin.Effects;

public sealed class CanvasMotion
{
    private EffectTimeline _timeline;
    private double _from;
    private double _to;
    private double _current;
    private bool _running;

    public CanvasMotion()
    {
        _timeline.Easing = EasingCurve.CubicBezier(0, 0, 0.2, 1);
    }

    public bool IsRunning => _running;

    public double Current => _current;

    public double Target => _to;

    public void Begin(double from, double to, long durationNanos)
    {
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

    public void Cancel() => _running = false;

    public bool Step(in FrameTick tick, out double value)
    {
        if (!_running)
        {
            value = _current;
            return false;
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
