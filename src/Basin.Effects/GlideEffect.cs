using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs KWin's glide open and close animation on one window. The window tilts about an edge in perspective, moves back and fades, through a transform node on its <see cref="TransformStack"/>.
/// </summary>
public sealed class GlideEffect
{
    private const string NodeName = "glide";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly GlideOptions _options;
    private EffectTimeline _timeline;
    private bool _hiding;
    private SceneTransform? _node;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlideEffect"/> class.
    /// </summary>
    /// <param name="options">The animation shape. The default value uses the default <see cref="GlideOptions"/>.</param>
    public GlideEffect(GlideOptions options = default) =>
        _options = options == default ? new GlideOptions() : options;

    /// <summary>
    /// Gets the options that the effect uses.
    /// </summary>
    public GlideOptions Options => _options;

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
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="hiding"><see langword="true"/> for a close, <see langword="false"/> for an open.</param>
    /// <param name="now">The tick that the animation starts at.</param>
    /// <param name="duration">The length of the animation.</param>
    /// <returns><see langword="false"/> when <paramref name="duration"/> is disabled, and then nothing starts.</returns>
    public bool Begin(TransformStack stack, bool hiding, in FrameTick now, AnimationDuration duration)
    {
        if (!Begin(stack, hiding, duration))
        {
            return false;
        }

        _timeline.Anchor(now);
        return true;
    }

    /// <summary>
    /// Starts an animation and applies its first pose. The first <see cref="Step"/> anchors the start one refresh interval before its tick.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="hiding"><see langword="true"/> for a close, <see langword="false"/> for an open.</param>
    /// <param name="duration">The length of the animation.</param>
    /// <returns><see langword="false"/> when <paramref name="duration"/> is disabled, and then nothing starts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Changes an open into a close or a close into an open, and continues from the current pose.
    /// </summary>
    /// <param name="now">The tick of the change.</param>
    public void Reverse(in FrameTick now)
    {
        _thread.Assert();
        _hiding = !_hiding;
        _timeline.Easing = _hiding ? EasingCurve.OutCurve : EasingCurve.InCurve;
        _timeline.RestartPreservingProgress(now);
    }

    /// <summary>
    /// Applies the pose for a tick. When an open ends, the node is removed. When a close ends, the node keeps the last pose until <see cref="End"/>.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
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
    /// <param name="stack">The transform stack of the window.</param>
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
