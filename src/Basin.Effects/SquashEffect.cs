using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs the squash minimize animation. The window scales onto the icon rectangle, moves onto its center and can fade.
/// </summary>
public sealed class SquashEffect
{
    private const string NodeName = "squash";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private EffectTimeline _timeline;
    private Box _window;
    private Box _icon;
    private bool _restoring;
    private SceneTransform? _node;

    /// <summary>
    /// Gets a value indicating whether an animation has a live node that <see cref="Step"/> still drives.
    /// </summary>
    public bool IsRunning => _node is { IsDestroyed: false };

    /// <summary>
    /// Gets a value indicating whether the current or last animation is a restore, which runs from the icon back to the window.
    /// </summary>
    public bool IsRestoring => _restoring;

    /// <summary>
    /// Gets or sets a value indicating whether the window fades out as it reaches the icon. The default is <see langword="true"/>.
    /// </summary>
    public bool Fade { get; set; } = true;

    /// <summary>
    /// Starts an animation that is anchored at a given tick.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="window">The window box, in the same space as <paramref name="icon"/>.</param>
    /// <param name="icon">The icon box.</param>
    /// <param name="restoring"><see langword="true"/> to run from the icon back to the window.</param>
    /// <param name="now">The tick that the animation starts at.</param>
    /// <param name="duration">The length of the animation.</param>
    /// <returns><see langword="false"/> when <paramref name="duration"/> is disabled or a box is empty, and then nothing starts.</returns>
    public bool Begin(
        TransformStack stack, in Box window, in Box icon, bool restoring, in FrameTick now, AnimationDuration duration)
    {
        if (!Begin(stack, window, icon, restoring, duration))
        {
            return false;
        }

        _timeline.Anchor(now);
        return true;
    }

    /// <summary>
    /// Starts an animation that the first <see cref="Step"/> anchors, and applies its first pose. A window with no icon cannot run it.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="window">The window box, in the same space as <paramref name="icon"/>.</param>
    /// <param name="icon">The icon box.</param>
    /// <param name="restoring"><see langword="true"/> to run from the icon back to the window.</param>
    /// <param name="duration">The length of the animation.</param>
    /// <returns><see langword="false"/> when <paramref name="duration"/> is disabled or a box is empty, and then nothing starts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
    public bool Begin(TransformStack stack, in Box window, in Box icon, bool restoring, AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (duration.IsDisabled || window.IsEmpty || icon.IsEmpty)
        {
            return false;
        }

        _window = window;
        _icon = icon;
        _restoring = restoring;
        _node = stack.Get(NodeName) ?? stack.Add(TransformStack.ZOrder.Transform2D, NodeName);
        _timeline.Easing = restoring ? EasingCurve.OutCubic : EasingCurve.InCubic;
        _timeline.Start(duration.Nanos);
        Apply(0);
        return true;
    }

    /// <summary>
    /// Applies the pose for a tick. When a restore ends, the node is removed. When a minimize ends, the node keeps the squashed pose until <see cref="End"/>.
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

        if (_restoring)
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

        var t = _restoring ? 1.0 - progress : progress;
        var bounds = node.ContentBounds;
        var scaleX = Interpolate(1.0, (double)_icon.Width / Math.Max(1, _window.Width), t);
        var scaleY = Interpolate(1.0, (double)_icon.Height / Math.Max(1, _window.Height), t);
        var moveX = Interpolate(0, _icon.X - _window.X - ((_window.Width - _icon.Width) / 2.0), t);
        var moveY = Interpolate(0, _icon.Y - _window.Y - ((_window.Height - _icon.Height) / 2.0), t);
        var centerX = bounds.X + (bounds.Width / 2.0);
        var centerY = bounds.Y + (bounds.Height / 2.0);

        node.Alpha = Fade ? (float)Math.Clamp(1.0 - t, 0, 1) : 1f;
        node.Matrix = RenderTransform.Multiply(
            RenderTransform.Translation(moveX, moveY),
            RenderTransform.Multiply(
                RenderTransform.Translation(centerX * (1 - scaleX), centerY * (1 - scaleY)),
                RenderTransform.Scale(scaleX, scaleY)));
    }

    private static double Interpolate(double from, double to, double progress) => from + ((to - from) * progress);
}
