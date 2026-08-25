using Basin.Scene;

namespace Basin.Effects;

public sealed class OpenCloseAnimation
{
    private readonly OpenCloseKind _kind;
    private EffectTimeline _timeline;
    private bool _hiding;
    private SceneTransform? _node;

    public OpenCloseAnimation(OpenCloseKind kind, EasingCurve? easing = null)
    {
        _kind = kind;
        _timeline.Easing = easing ?? EasingCurve.Sigmoid;
    }

    public bool IsRunning => _node is { IsDestroyed: false };

    public double InScale { get; set; } = 0.6;

    public double OutScale { get; set; } = 0.6;

    public bool IsHiding => _hiding;

    public void Begin(TransformStack stack, bool hiding, in FrameTick now, long durationNanos)
    {
        Begin(stack, hiding, durationNanos);
        _timeline.Anchor(now);
    }

    public bool Begin(TransformStack stack, bool hiding, in FrameTick now, AnimationDuration duration)
    {
        if (!Begin(stack, hiding, duration))
        {
            return false;
        }

        _timeline.Anchor(now);
        return true;
    }

    public bool Begin(TransformStack stack, bool hiding, AnimationDuration duration)
    {
        if (duration.IsDisabled)
        {
            return false;
        }

        Begin(stack, hiding, duration.Nanos);
        return true;
    }

    public void Begin(TransformStack stack, bool hiding, long durationNanos)
    {
        _hiding = hiding;
        _node = stack.Get("open-close") ?? stack.Add(TransformStack.ZOrder.Transform2D, "open-close");
        _timeline.Start(durationNanos);
        Apply(0);
    }

    public void Reverse(in FrameTick now)
    {
        _hiding = !_hiding;
        _timeline.RestartPreservingProgress(now);
    }

    public bool Step(TransformStack stack, in FrameTick tick)
    {
        if (_node is not { IsDestroyed: false })
        {
            return false;
        }

        var progress = _timeline.Progress(tick);
        Apply(progress);
        if (_timeline.Running(tick))
        {
            return true;
        }

        if (!_hiding)
        {
            stack.Remove("open-close");
        }

        _node = null;
        return false;
    }

    private void Apply(double progress)
    {
        if (_node is not { IsDestroyed: false } node)
        {
            return;
        }

        var visible = _hiding ? 1.0 - progress : progress;
        visible = visible switch
        {
            > 0.999 => 1.0,
            < 0.001 => 0.0,
            _ => visible,
        };

        node.Alpha = (float)visible;
        if (_kind == OpenCloseKind.Zoom)
        {
            var bounds = node.ContentBounds;
            var rest = Math.Clamp(_hiding ? OutScale : InScale, 0, 1);
            var scale = rest + ((1 - rest) * visible);
            var centerX = bounds.X + (bounds.Width / 2.0);
            var centerY = bounds.Y + (bounds.Height / 2.0);
            node.Matrix = RenderTransform.Multiply(
                RenderTransform.Translation(centerX * (1 - scale), centerY * (1 - scale)),
                RenderTransform.Scale(scale, scale));
        }
    }
}
