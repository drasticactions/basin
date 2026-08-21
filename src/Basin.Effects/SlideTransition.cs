using Basin.Scene;

namespace Basin.Effects;

public sealed class SlideTransition
{
    private const string Name = "workspace-slide";

    private readonly long _nanos;
    private TransformStack? _outgoing;
    private TransformStack? _incoming;
    private SceneTransform? _outgoingNode;
    private SceneTransform? _incomingNode;
    private double _outFrom, _outTo, _outApplied;
    private double _inFrom, _inTo, _inApplied;
    private double _width;
    private int _side;
    private bool _interactive;
    private EffectTimeline _timeline;

    public SlideTransition(long durationNanos = 200_000_000)
    {
        _nanos = durationNanos;
        _timeline.Easing = EasingCurve.CubicBezier(0, 0, 0.2, 1);
    }

    public bool IsActive => _outgoing is not null;

    public bool IsInteractive => _interactive;

    public bool IsAnimating => IsActive && !_interactive;

    public void Begin(TransformStack outgoing, TransformStack incoming, in Box area, int direction)
    {
        ArgumentNullException.ThrowIfNull(outgoing);
        ArgumentNullException.ThrowIfNull(incoming);
        var width = Math.Max(1, area.Width);
        var side = direction >= 0 ? 1 : -1;
        var outStart = OffsetOf(outgoing, 0);
        var inStart = OffsetOf(incoming, side * width);
        Clear();

        _outgoing = outgoing;
        _incoming = incoming;
        _outgoingNode = outgoing.Get(Name) ?? outgoing.Add(TransformStack.ZOrder.Transform2D, Name);
        _incomingNode = incoming.Get(Name) ?? incoming.Add(TransformStack.ZOrder.Transform2D, Name);
        _outFrom = _outApplied = outStart;
        _outTo = -side * width;
        _inFrom = _inApplied = inStart;
        _inTo = 0;
        _width = width;
        _side = side;
        _interactive = false;
        Apply();
        _timeline.Start(_nanos);
    }

    public void BeginInteractive(TransformStack outgoing, TransformStack? incoming, in Box area, int direction)
    {
        ArgumentNullException.ThrowIfNull(outgoing);
        var width = Math.Max(1, area.Width);
        var side = direction >= 0 ? 1 : -1;
        var outStart = OffsetOf(outgoing, 0);
        var inStart = incoming is null ? side * width : OffsetOf(incoming, side * width);
        Clear();

        _outgoing = outgoing;
        _incoming = incoming;
        _outgoingNode = outgoing.Get(Name) ?? outgoing.Add(TransformStack.ZOrder.Transform2D, Name);
        _incomingNode = incoming?.Get(Name) ?? incoming?.Add(TransformStack.ZOrder.Transform2D, Name);
        _outFrom = _outApplied = outStart;
        _inFrom = _inApplied = inStart;
        _outTo = -side * width;
        _inTo = 0;
        _width = width;
        _side = side;
        _interactive = true;
        Apply();
    }

    public double Progress
    {
        get => _width <= 0 ? 0 : -_outApplied / (_side * _width);

        set
        {
            if (!_interactive)
            {
                return;
            }

            var travel = value * _side * _width;
            _outApplied = -travel;
            _inApplied = (_side * _width) - travel;
            Apply();
        }
    }

    public void Settle(bool commit)
    {
        if (!_interactive)
        {
            return;
        }

        _interactive = false;
        commit &= _incoming is not null;
        _outFrom = _outApplied;
        _inFrom = _inApplied;
        _outTo = commit ? -_side * _width : 0;
        _inTo = commit ? 0 : _side * _width;
        _timeline.Start(_nanos);
    }

    public bool Step(in FrameTick tick)
    {
        if (_outgoing is null || _interactive)
        {
            return false;
        }

        if (_outgoingNode is not { IsDestroyed: false }
            || (_incoming is not null && _incomingNode is not { IsDestroyed: false }))
        {
            Clear();
            return false;
        }

        var t = _timeline.Progress(tick);
        _outApplied = _outFrom + ((_outTo - _outFrom) * t);
        _inApplied = _inFrom + ((_inTo - _inFrom) * t);
        Apply();

        if (!_timeline.Running(tick))
        {
            Clear();
            return false;
        }

        return true;
    }

    private double OffsetOf(TransformStack stack, double fallback) =>
        ReferenceEquals(stack, _outgoing) ? _outApplied
        : ReferenceEquals(stack, _incoming) ? _inApplied
        : fallback;

    private void Apply()
    {
        if (_outgoingNode is { IsDestroyed: false } outgoing)
        {
            outgoing.Matrix = RenderTransform.Translation(_outApplied, 0);
        }

        if (_incomingNode is { IsDestroyed: false } incoming)
        {
            incoming.Matrix = RenderTransform.Translation(_inApplied, 0);
        }
    }

    private void Clear()
    {
        _outgoing?.Remove(Name);
        _incoming?.Remove(Name);
        _outgoing = null;
        _incoming = null;
        _outgoingNode = null;
        _incomingNode = null;
    }
}
