using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class StretchEffect
{
    private const string NodeName = "stretch";

    private const long SettleNanos = 2_000_000_000;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private EffectTimeline _timeline;
    private Box _from;
    private Box _to;
    private SceneTransform? _node;
    private SceneTransform? _fade;
    private SceneSnapshot? _previous;
    private Box _captured;
    private double _holdX;
    private double _holdY;
    private bool _sizing;
    private Box _unsettled;
    private EffectTimeline _settle;

    public bool IsRunning => _node is { IsDestroyed: false };

    public bool Capture(
        TransformStack stack, in Box frame, in Box from, in Box current, int originX, int originY,
        AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (duration.IsDisabled || frame.IsEmpty)
        {
            return false;
        }

        _captured = frame;
        _holdX = from.X - current.X;
        _holdY = from.Y - current.Y;
        _sizing = false;
        _node = stack.Get(NodeName) ?? stack.Add(TransformStack.ZOrder.Transform2D, NodeName);
        ReleasePrevious();
        _previous = SceneSnapshot.Capture(_node, stack.Root);
        _fade = new SceneTransform(_node);
        _previous.Tree.Reparent(_fade);
        _previous.Tree.SetPosition(0, 0);
        _timeline.Easing = EasingCurve.OutCubic;
        _timeline.Start(duration.Nanos);
        Apply(0, frame, originX, originY);
        return true;
    }

    public bool Begin(
        TransformStack stack, in Box frame, in Box from, in Box to, int originX, int originY,
        AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (duration.IsDisabled || from.IsEmpty || to.IsEmpty ||
            (from.Width == to.Width && from.Height == to.Height))
        {
            return false;
        }

        _from = from;
        _to = to;
        _sizing = true;
        _unsettled = frame;
        _settle = default;
        _node ??= stack.Get(NodeName) ?? stack.Add(TransformStack.ZOrder.Transform2D, NodeName);
        _timeline.Easing = EasingCurve.OutCubic;
        _timeline.Start(duration.Nanos);
        Apply(0, frame, originX, originY);
        return true;
    }

    public bool Step(TransformStack stack, in Box frame, int originX, int originY, in FrameTick tick)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (_node is not { IsDestroyed: false })
        {
            return false;
        }

        Apply(_timeline.Progress(tick), frame, originX, originY);
        if (_timeline.Running(tick))
        {
            return true;
        }

        if (_sizing &&
            frame.Width == _unsettled.Width && frame.Height == _unsettled.Height &&
            (frame.Width != _to.Width || frame.Height != _to.Height))
        {
            if (!_settle.IsStarted)
            {
                _settle.Start(SettleNanos);
                _settle.Anchor(tick);
            }

            if (_settle.Running(tick))
            {
                return true;
            }
        }

        ReleasePrevious();
        stack.Remove(NodeName);
        _node = null;
        return false;
    }

    public void End(TransformStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        ReleasePrevious();
        stack.Remove(NodeName);
        _node = null;
    }

    private void Apply(double progress, in Box frame, int originX, int originY)
    {
        if (_node is not { IsDestroyed: false } node || frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        if (!_sizing)
        {
            node.Matrix = RenderTransform.Translation(_holdX, _holdY);
        }
        else
        {
            var scaleX = Interpolate(_from.Width, _to.Width, progress) / frame.Width;
            var scaleY = Interpolate(_from.Height, _to.Height, progress) / frame.Height;
            var targetX = Interpolate(_from.X, _to.X, progress);
            var targetY = Interpolate(_from.Y, _to.Y, progress);

            node.Matrix = RenderTransform.Multiply(
                RenderTransform.Translation(
                    targetX - originX - (frame.X * scaleX),
                    targetY - originY - (frame.Y * scaleY)),
                RenderTransform.Scale(scaleX, scaleY));
        }

        if (_fade is not { IsDestroyed: false } fade || _captured.Width <= 0 || _captured.Height <= 0)
        {
            return;
        }

        fade.Alpha = _sizing ? (float)Math.Clamp(1.0 - progress, 0, 1) : 1f;
        var fitX = (double)frame.Width / _captured.Width;
        var fitY = (double)frame.Height / _captured.Height;
        fade.Matrix = RenderTransform.Multiply(
            RenderTransform.Translation(frame.X - (_captured.X * fitX), frame.Y - (_captured.Y * fitY)),
            RenderTransform.Scale(fitX, fitY));
    }

    public void Release() => ReleasePrevious();

    private void ReleasePrevious()
    {
        _previous?.Destroy();
        _previous = null;
        if (_fade is { IsDestroyed: false } fade)
        {
            fade.Destroy();
        }

        _fade = null;
    }

    private static double Interpolate(double from, double to, double progress) => from + ((to - from) * progress);
}
