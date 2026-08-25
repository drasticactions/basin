using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class SquashEffect
{
    private const string NodeName = "squash";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private EffectTimeline _timeline;
    private Box _window;
    private Box _icon;
    private bool _restoring;
    private SceneTransform? _node;

    public bool IsRunning => _node is { IsDestroyed: false };

    public bool IsRestoring => _restoring;

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

        node.Alpha = (float)Math.Clamp(1.0 - t, 0, 1);
        node.Matrix = RenderTransform.Multiply(
            RenderTransform.Translation(moveX, moveY),
            RenderTransform.Multiply(
                RenderTransform.Translation(centerX * (1 - scaleX), centerY * (1 - scaleY)),
                RenderTransform.Scale(scaleX, scaleY)));
    }

    private static double Interpolate(double from, double to, double progress) => from + ((to - from) * progress);
}
