using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class GlideEffect
{
    private const string NodeName = "glide";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly GlideOptions _options;
    private EffectTimeline _timeline;
    private bool _hiding;
    private SceneTransform? _node;

    public GlideEffect(GlideOptions options = default) =>
        _options = options == default ? new GlideOptions() : options;

    public GlideOptions Options => _options;

    public bool IsRunning => _node is { IsDestroyed: false };

    public bool IsHiding => _hiding;

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
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (duration.IsDisabled)
        {
            return false;
        }

        _hiding = hiding;
        _node = stack.Get(NodeName) ?? stack.Add(TransformStack.ZOrder.Transform3D, NodeName);
        _timeline.Easing = hiding ? EasingCurve.OutCurve : EasingCurve.InCurve;
        _timeline.Start(duration.Nanos);
        Apply(0);
        return true;
    }

    public void Reverse(in FrameTick now)
    {
        _thread.Assert();
        _hiding = !_hiding;
        _timeline.Easing = _hiding ? EasingCurve.OutCurve : EasingCurve.InCurve;
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

        var edge = _hiding ? _options.OutEdge : _options.InEdge;
        var angle = _hiding
            ? Interpolate(0, _options.OutAngle, progress)
            : Interpolate(_options.InAngle, 0, progress);
        var distance = _hiding
            ? Interpolate(0, _options.OutDistance, progress)
            : Interpolate(_options.InDistance, 0, progress);
        var opacity = _hiding
            ? Interpolate(1.0, _options.OutOpacity, progress)
            : Interpolate(_options.InOpacity, 1.0, progress);

        node.Alpha = (float)Math.Clamp(opacity, 0, 1);
        node.Matrix = Projection.Frustum(node.ContentBounds, edge, angle, distance);
    }

    private static double Interpolate(double from, double to, double progress) => from + ((to - from) * progress);
}
