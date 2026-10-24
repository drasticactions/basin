using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs the sheet animation for a modal dialog. The dialog unfolds from a hinge at its top edge and fades in, and the reverse closes it.
/// </summary>
public sealed class SheetEffect
{
    private const string NodeName = "sheet";

    private const double HingeAngle = 60.0;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private EffectTimeline _timeline;
    private bool _hiding;
    private double _parentDrop;
    private SceneTransform? _node;

    /// <summary>
    /// Gets a value indicating whether an animation has a live node that <see cref="Step"/> still drives.
    /// </summary>
    public bool IsRunning => _node is { IsDestroyed: false };

    /// <summary>
    /// Gets a value indicating whether the current or last animation is a close.
    /// </summary>
    public bool IsHiding => _hiding;

    /// <summary>
    /// Starts an animation that is anchored at a given tick.
    /// </summary>
    /// <param name="stack">The transform stack of the dialog.</param>
    /// <param name="hiding"><see langword="true"/> for a close, <see langword="false"/> for an open.</param>
    /// <param name="parentDrop">The distance, in pixels, that the dialog moves up when it is folded.</param>
    /// <param name="now">The tick that the animation starts at.</param>
    /// <param name="duration">The length of the animation.</param>
    /// <returns><see langword="false"/> when <paramref name="duration"/> is disabled, and then nothing starts.</returns>
    public bool Begin(TransformStack stack, bool hiding, double parentDrop, in FrameTick now, AnimationDuration duration)
    {
        if (!Begin(stack, hiding, parentDrop, duration))
        {
            return false;
        }

        _timeline.Anchor(now);
        return true;
    }

    /// <summary>
    /// Starts a linear animation and applies its first pose. The first <see cref="Step"/> anchors the start.
    /// </summary>
    /// <param name="stack">The transform stack of the dialog.</param>
    /// <param name="hiding"><see langword="true"/> for a close, <see langword="false"/> for an open.</param>
    /// <param name="parentDrop">The distance, in pixels, that the dialog moves up when it is folded.</param>
    /// <param name="duration">The length of the animation.</param>
    /// <returns><see langword="false"/> when <paramref name="duration"/> is disabled, and then nothing starts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Changes an open into a close or a close into an open, and continues from the current pose.
    /// </summary>
    /// <param name="now">The tick of the change.</param>
    public void Reverse(in FrameTick now)
    {
        _thread.Assert();
        _hiding = !_hiding;
        _timeline.RestartPreservingProgress(now);
    }

    /// <summary>
    /// Applies the pose for a tick. When an open ends, the node is removed. When a close ends, the node keeps the folded pose until <see cref="End"/>.
    /// </summary>
    /// <param name="stack">The transform stack of the dialog.</param>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the animation runs, and <see langword="false"/> when it ended or no animation runs.</returns>
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

    /// <summary>
    /// Removes the transform node at once, whatever the progress.
    /// </summary>
    /// <param name="stack">The transform stack of the dialog.</param>
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
