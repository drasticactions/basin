using Basin;
using Basin.Scene;

namespace EightWm;

internal struct Tween
{
    private AnimationSpec _spec;
    private long _startMillis;
    private uint _delayMs;
    private bool _running;

    public readonly bool IsRunning => _running;

    public readonly Animation Name => _spec.Name;

    public double Offset { get; private set; }

    public double Scale { get; private set; }

    public float Alpha { get; private set; }

    public double OffsetScale { get; set; }

    public void Start(in AnimationSpec spec, long nowMillis, int index = 0)
    {
        _spec = spec;
        _startMillis = nowMillis;
        _delayMs = spec.DelayFor(index);
        _running = true;
        if (OffsetScale == 0)
        {
            OffsetScale = 1;
        }

        Sample(0);
    }

    public void Stop()
    {
        _running = false;
        Offset = 0;
        Scale = 1;
        Alpha = 1;
    }

    public void Settle()
    {
        if (!_running)
        {
            return;
        }

        Sample(_spec.DurationMs + _delayMs);
        _running = false;
    }

    public bool Advance(long nowMillis)
    {
        if (!_running)
        {
            return false;
        }

        var elapsed = nowMillis - _startMillis;
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        var total = _spec.DurationMs + _delayMs;
        Sample(elapsed);
        if (elapsed >= total)
        {
            _running = false;
        }

        return true;
    }

    public readonly void Apply(SceneTransform node)
    {
        if (node is null || node.IsDestroyed)
        {
            return;
        }

        var offset = Offset * OffsetScale;
        var scale = Scale <= 0 ? 1 : Scale;
        var translateX = _spec.Axis == MotionAxis.X ? offset : 0;
        var translateY = _spec.Axis == MotionAxis.Y ? offset : 0;

        node.Matrix = scale == 1
            ? RenderTransform.Translation(translateX, translateY)
            : RenderTransform.Multiply(
                RenderTransform.Translation(translateX, translateY), RenderTransform.Scale(scale, scale));
        node.Alpha = Alpha;
    }

    public static void Reset(SceneTransform? node)
    {
        if (node is { IsDestroyed: false })
        {
            node.Matrix = RenderTransform.Identity;
            node.Alpha = 1f;
        }
    }

    private void Sample(long elapsed)
    {
        Offset = Value(_spec.Offset, elapsed, fallback: 0);
        Scale = Value(_spec.Scale, elapsed, fallback: 1);
        Alpha = (float)Value(_spec.Opacity, elapsed, fallback: 1);
    }

    private readonly double Value(in Track track, long elapsed, double fallback)
    {
        if (track.IsEmpty)
        {
            return fallback;
        }

        var start = _delayMs + track.DelayMs;
        if (elapsed <= start)
        {
            return track.From;
        }

        var progress = (elapsed - start) / (double)track.DurationMs;
        if (progress >= 1)
        {
            return track.To;
        }

        return track.From + ((track.To - track.From) * Curves.Evaluate(track.Curve, progress));
    }
}
