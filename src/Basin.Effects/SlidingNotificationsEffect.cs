using Basin.Diagnostics;
using Basin.Kinematics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs KWin's sliding notifications effect. A notification slides in or out and the others reflow, each through a spring-damper translation.
/// </summary>
public sealed class SlidingNotificationsEffect
{
    /// <summary>
    /// The spring constant of a slide, before the division by the animation factor.
    /// </summary>
    public const double SlideSpringConstant = 900.0;

    /// <summary>
    /// The spring constant of a reflow, before the division by the animation factor.
    /// </summary>
    public const double ReflowSpringConstant = 2000.0;

    private const string SlideNode = "notification-slide";

    private const string ReflowNode = "notification-reflow";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<Entry> _entries = [];
    private long _lastNanos;
    private bool _hasLast;

    /// <summary>
    /// Gets a value indicating whether a slide or a reflow still moves.
    /// </summary>
    public bool IsActive => _entries.Count > 0;

    /// <summary>
    /// Starts a slide of a notification. It replaces an earlier slide of the same window.
    /// </summary>
    /// <param name="stack">The transform stack of the notification.</param>
    /// <param name="fromX">The start horizontal offset, in pixels.</param>
    /// <param name="fromY">The start vertical offset, in pixels.</param>
    /// <param name="toX">The end horizontal offset, in pixels.</param>
    /// <param name="toY">The end vertical offset, in pixels.</param>
    /// <param name="durationFactor">The animation factor of the desktop. It divides the spring constant. Zero or less jumps to the end.</param>
    /// <param name="removeWhenSettled"><see langword="true"/> to remove the node at the end. <see langword="false"/> keeps the end offset, for example for a slide out.</param>
    /// <returns><see langword="true"/> when a motion started, and <see langword="false"/> when it jumped to the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
    public bool Slide(
        TransformStack stack,
        double fromX,
        double fromY,
        double toX,
        double toY,
        double durationFactor,
        bool removeWhenSettled)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        return Begin(stack, SlideNode, SlideSpringConstant, fromX, fromY, toX, toY, durationFactor, removeWhenSettled);
    }

    /// <summary>
    /// Starts a vertical reflow of a notification from an offset back to its place. The node stays at offset zero after the end.
    /// </summary>
    /// <param name="stack">The transform stack of the notification.</param>
    /// <param name="fromY">The start vertical offset, in pixels.</param>
    /// <param name="durationFactor">The animation factor of the desktop. It divides the spring constant. Zero or less jumps to the end.</param>
    /// <returns><see langword="true"/> when a motion started, and <see langword="false"/> when it jumped to the end.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
    public bool Reflow(TransformStack stack, double fromY, double durationFactor)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        return Begin(stack, ReflowNode, ReflowSpringConstant, 0, fromY, 0, 0, durationFactor, removeWhenSettled: false);
    }

    /// <summary>
    /// Advances every motion by the time since the last tick. A motion that stops snaps to its end offset.
    /// </summary>
    /// <param name="tick">The frame tick. The first tick only starts the clock.</param>
    /// <returns>The value of <see cref="IsActive"/> after the step.</returns>
    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (!_hasLast)
        {
            _lastNanos = tick.TargetPresentNanos;
            _hasLast = true;
            return IsActive;
        }

        var elapsed = (tick.TargetPresentNanos - _lastNanos) / 1_000_000.0;
        _lastNanos = tick.TargetPresentNanos;
        if (elapsed <= 0)
        {
            return IsActive;
        }

        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.Node.IsDestroyed)
            {
                _entries.RemoveAt(i);
                continue;
            }

            entry.X.Advance(elapsed);
            entry.Y.Advance(elapsed);
            Apply(entry);
            if (entry.X.IsMoving || entry.Y.IsMoving)
            {
                continue;
            }

            entry.X.SetPosition(entry.X.Anchor);
            entry.Y.SetPosition(entry.Y.Anchor);
            Apply(entry);
            if (!entry.KeepWhenSettled)
            {
                entry.Stack.Remove(entry.Name);
            }

            _entries.RemoveAt(i);
        }

        return IsActive;
    }

    /// <summary>
    /// Stops every motion of a notification and removes its nodes.
    /// </summary>
    /// <param name="stack">The transform stack of the notification.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
    public void Release(TransformStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_entries[i].Stack, stack))
            {
                _entries[i].Stack.Remove(_entries[i].Name);
                _entries.RemoveAt(i);
            }
        }
    }

    private bool Begin(
        TransformStack stack,
        string name,
        double constant,
        double fromX,
        double fromY,
        double toX,
        double toY,
        double durationFactor,
        bool removeWhenSettled)
    {
        var node = stack.Get(name) ?? stack.Add(TransformStack.ZOrder.Transform2D, name);
        var scaled = durationFactor > 0 ? constant / durationFactor : double.PositiveInfinity;
        var entry = new Entry
        {
            Stack = stack,
            Name = name,
            Node = node,
            X = new SpringMotion(scaled, 1.0) { Anchor = toX },
            Y = new SpringMotion(scaled, 1.0) { Anchor = toY },
            KeepWhenSettled = !removeWhenSettled,
        };
        entry.X.SetPosition(fromX);
        entry.Y.SetPosition(fromY);
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_entries[i].Stack, stack) && _entries[i].Name == name)
            {
                _entries.RemoveAt(i);
            }
        }

        _entries.Add(entry);
        Apply(entry);
        if (double.IsInfinity(scaled))
        {
            entry.X.Advance(1);
            entry.Y.Advance(1);
            Apply(entry);
            _entries.Remove(entry);
            if (removeWhenSettled)
            {
                stack.Remove(name);
            }

            return false;
        }

        return true;
    }

    private static void Apply(Entry entry)
    {
        if (!entry.Node.IsDestroyed)
        {
            entry.Node.Matrix = RenderTransform.Translation(entry.X.Position, entry.Y.Position);
        }
    }

    private sealed class Entry
    {
        public required TransformStack Stack;
        public required string Name;
        public required SceneTransform Node;
        public SpringMotion X;
        public SpringMotion Y;
        public bool KeepWhenSettled;
    }
}
