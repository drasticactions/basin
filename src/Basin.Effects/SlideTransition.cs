using Basin.Kinematics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Slides one workspace out and another in, side by side, through a translation on each workspace tree. It can run on a timeline or follow a gesture and then settle.
/// </summary>
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
    private SpringHandoff _spring;
    private bool _springing;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlideTransition"/> class.
    /// </summary>
    /// <param name="durationNanos">The length of a timed slide or a settle, in nanoseconds.</param>
    public SlideTransition(long durationNanos = 200_000_000)
    {
        _nanos = durationNanos;
        _timeline.Easing = EasingCurve.CubicBezier(0, 0, 0.2, 1);
    }

    /// <summary>
    /// Gets or sets the stiffness of the critically damped spring that <see cref="Settle(bool, double)"/> uses. The default is 800.
    /// </summary>
    public double SpringStiffness { get; set; } = 800;

    /// <summary>
    /// Gets a value indicating whether a slide is in progress, timed or interactive.
    /// </summary>
    public bool IsActive => _outgoing is not null;

    /// <summary>
    /// Gets a value indicating whether a gesture drives the slide through <see cref="Progress"/>.
    /// </summary>
    public bool IsInteractive => _interactive;

    /// <summary>
    /// Gets a value indicating whether a slide is in progress and <see cref="Step"/> drives it.
    /// </summary>
    public bool IsAnimating => IsActive && !_interactive;

    /// <summary>
    /// Starts a timed slide. A workspace that is already part of a slide starts from its current offset.
    /// </summary>
    /// <param name="outgoing">The transform stack of the workspace that leaves.</param>
    /// <param name="incoming">The transform stack of the workspace that arrives.</param>
    /// <param name="area">The output area. Its width is the travel.</param>
    /// <param name="direction">Zero or more moves the outgoing workspace to the left, and a negative value moves it to the right.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outgoing"/> or <paramref name="incoming"/> is <see langword="null"/>.</exception>
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
        _springing = false;
        Apply();
        _timeline.Start(_nanos);
    }

    /// <summary>
    /// Starts a slide that a gesture drives through <see cref="Progress"/>. It does not move until the progress changes.
    /// </summary>
    /// <param name="outgoing">The transform stack of the workspace that leaves.</param>
    /// <param name="incoming">The transform stack of the workspace that arrives, or <see langword="null"/> at the last workspace. Without it, a settle always returns.</param>
    /// <param name="area">The output area. Its width is the travel.</param>
    /// <param name="direction">Zero or more moves the outgoing workspace to the left, and a negative value moves it to the right.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outgoing"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets or sets the fraction of the travel that the outgoing workspace covered, where 1 is a full switch. Only an interactive slide accepts a new value.
    /// </summary>
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

    /// <summary>
    /// Gets the horizontal offset that the slide applies to a workspace now.
    /// </summary>
    /// <param name="stack">The transform stack of the workspace.</param>
    /// <param name="offset">The offset in layout units, or zero when the workspace is not part of the slide.</param>
    /// <returns><see langword="false"/> when no slide runs or the workspace is not part of it.</returns>
    public bool TryGetOffset(TransformStack stack, out double offset)
    {
        offset = 0;
        if (_outgoing is null || stack is null)
        {
            return false;
        }

        if (ReferenceEquals(stack, _outgoing))
        {
            offset = _outApplied;
            return true;
        }

        if (ReferenceEquals(stack, _incoming))
        {
            offset = _inApplied;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ends a gesture and runs the slide on the timeline to the end or back to the start. It does nothing when the slide is not interactive.
    /// </summary>
    /// <param name="commit"><see langword="true"/> to finish the switch. It counts as <see langword="false"/> when there is no incoming workspace.</param>
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
        _springing = false;
        _timeline.Start(_nanos);
    }

    /// <summary>
    /// Ends a gesture and hands the slide to a critically damped spring that starts at the gesture velocity. It does nothing when the slide is not interactive.
    /// </summary>
    /// <param name="commit"><see langword="true"/> to finish the switch. It counts as <see langword="false"/> when there is no incoming workspace.</param>
    /// <param name="velocity">The gesture velocity, in travel fractions per second.</param>
    public void Settle(bool commit, double velocity)
    {
        if (!_interactive)
        {
            return;
        }

        Settle(commit);
        commit &= _incoming is not null;
        var travel = -_outApplied * _side;
        _spring = new SpringHandoff(SpringStiffness, 1.0);
        _spring.Start(travel, 0, velocity * _width, 0, commit ? _width : 0, 0);
        _springing = true;
    }

    /// <summary>
    /// Applies the offsets for a tick. At the end it removes both translation nodes. A destroyed node ends the slide at once.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the timeline or the spring runs. It is <see langword="false"/> during a gesture.</returns>
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

        if (_springing)
        {
            var moving = _spring.Step(tick);
            var travel = Math.Clamp(_spring.X, 0, _width);
            _outApplied = -_side * travel;
            _inApplied = (_side * _width) - (_side * travel);
            Apply();
            if (!moving)
            {
                Clear();
                return false;
            }

            return true;
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
        _springing = false;
    }
}
