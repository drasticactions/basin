using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class SheetEffect
{
    private const string NodeName = "sheet";

    private const double HingeAngle = 60.0;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private EffectTimeline _timeline;
    private bool _hiding;
    private double _parentDrop;
    private SceneTransform? _node;

    public bool IsRunning => _node is { IsDestroyed: false };

    public bool IsHiding => _hiding;

    public bool Begin(TransformStack stack, bool hiding, double parentDrop, in FrameTick now, AnimationDuration duration)
    {
        if (!Begin(stack, hiding, parentDrop, duration))
        {
            return false;
        }

        _timeline.Anchor(now);
        return true;
    }

    public bool Begin(TransformStack stack, bool hiding, double parentDrop, AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (duration.IsDisabled)
        {
            return false;
        }

        _hiding = hiding;
        _parentDrop = parentDrop;
        _node = stack.Get(NodeName) ?? stack.Add(TransformStack.ZOrder.Transform3D, NodeName);
        _timeline.Easing = EasingCurve.Linear;
        _timeline.Start(duration.Nanos);
        Apply(0);
        return true;
    }

    public void Reverse(in FrameTick now)
    {
        _thread.Assert();
        _hiding = !_hiding;
        _timeline.RestartPreservingProgress(now);
    }

    public bool Step(TransformStack stack, in FrameTick tick)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (_node is not { IsDestroyed: false })
        {
            return false;
        }

        Apply(_timeline.Progress(tick));
        if (_timeline.Running(tick))
        {
            return true;
        }

        if (!_hiding)
        {
            stack.Remove(NodeName);
        }

        _node = null;
        return false;
    }

    public void End(TransformStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        stack.Remove(NodeName);
        _node = null;
    }

    private void Apply(double progress)
    {
        if (_node is not { IsDestroyed: false } node)
        {
            return;
        }

        var bounds = node.ContentBounds;
        var t = _hiding ? 1.0 - progress : progress;
        var angle = HingeAngle * (1.0 - t) * Math.PI / 180.0;
        var sin = Math.Sin(angle);
        var cos = Math.Cos(angle);
        var drop = _parentDrop * (1.0 - t);
        var height = (double)bounds.Height;
        var width = (double)bounds.Width;

        (double X, double Y) Corner(double x, double y)
        {
            var hinged = y * cos;
            var depth = y * sin;
            return Projection.FrustumPoint(bounds, x, t * (hinged - drop), t * depth);
        }

        node.Alpha = (float)Math.Clamp(t, 0, 1);
        node.Matrix = Projection.MapRect(
            bounds,
            Corner(0, 0),
            Corner(width, 0),
            Corner(0, height),
            Corner(width, height));
    }
}
