using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Fades, and can also scale, one window as it opens or closes. It drives a 2D transform node on the window's <see cref="TransformStack"/>.
/// </summary>
public sealed class OpenCloseAnimation
{
    private readonly OpenCloseKind _kind;
    private EffectTimeline _timeline;
    private bool _hiding;
    private SceneTransform? _node;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenCloseAnimation"/> class.
    /// </summary>
    /// <param name="kind">The shape of the animation.</param>
    /// <param name="easing">The easing of the progress. <see langword="null"/> uses <see cref="EasingCurve.Sigmoid"/>.</param>
    public OpenCloseAnimation(OpenCloseKind kind, EasingCurve? easing = null)
    {
        _kind = kind;
        _timeline.Easing = easing ?? EasingCurve.Sigmoid;
    }

    /// <summary>
    /// Gets a value indicating whether an animation has a live node that <see cref="Step"/> still drives.
    /// </summary>
    public bool IsRunning => _node is { IsDestroyed: false };

    /// <summary>
    /// Gets or sets the scale that a zoom open starts from, clamped between 0 and 1 when used. The default is 0.6.
    /// </summary>
    public double InScale { get; set; } = 0.6;

    /// <summary>
    /// Gets or sets the scale that a zoom close ends at, clamped between 0 and 1 when used. The default is 0.6.
    /// </summary>
    public double OutScale { get; set; } = 0.6;

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
    /// <param name="durationNanos">The length of the animation, in nanoseconds.</param>
    public void Begin(TransformStack stack, bool hiding, in FrameTick now, long durationNanos)
    {
        Begin(stack, hiding, durationNanos);
        _timeline.Anchor(now);
    }

    /// <summary>
    /// Starts an animation that is anchored at a given tick, unless the duration is disabled.
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
    /// Starts an animation that the first <see cref="Step"/> anchors, unless the duration is disabled.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="hiding"><see langword="true"/> for a close, <see langword="false"/> for an open.</param>
    /// <param name="duration">The length of the animation.</param>
    /// <returns><see langword="false"/> when <paramref name="duration"/> is disabled, and then nothing starts.</returns>
    public bool Begin(TransformStack stack, bool hiding, AnimationDuration duration)
    {
        if (duration.IsDisabled)
        {
            return false;
        }

        Begin(stack, hiding, duration.Nanos);
        return true;
    }

    /// <summary>
    /// Starts an animation that the first <see cref="Step"/> anchors, and applies its first pose. A running animation restarts.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="hiding"><see langword="true"/> for a close, <see langword="false"/> for an open.</param>
    /// <param name="durationNanos">The length of the animation, in nanoseconds.</param>
    public void Begin(TransformStack stack, bool hiding, long durationNanos)
    {
        _hiding = hiding;
        _node = stack.Get("open-close") ?? stack.Add(TransformStack.ZOrder.Transform2D, "open-close");
        _timeline.Start(durationNanos);
        Apply(0);
    }

    /// <summary>
    /// Changes an open into a close or a close into an open, and continues from the current pose.
    /// </summary>
    /// <param name="now">The tick of the change.</param>
    public void Reverse(in FrameTick now)
    {
        _hiding = !_hiding;
        _timeline.RestartPreservingProgress(now);
    }

    /// <summary>
    /// Applies the pose for a tick. When an open ends, the node is removed. When a close ends, the node keeps the hidden pose.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the animation runs, and <see langword="false"/> when it ended or no animation runs.</returns>
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
