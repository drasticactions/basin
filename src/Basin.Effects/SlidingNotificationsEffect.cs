using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class SlidingNotificationsEffect
{
    public const double SlideSpringConstant = 900.0;

    public const double ReflowSpringConstant = 2000.0;

    private const string SlideNode = "notification-slide";

    private const string ReflowNode = "notification-reflow";

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<Entry> _entries = [];
    private long _lastNanos;
    private bool _hasLast;

    public bool IsActive => _entries.Count > 0;

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

    public bool Reflow(TransformStack stack, double fromY, double durationFactor)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        return Begin(stack, ReflowNode, ReflowSpringConstant, 0, fromY, 0, 0, durationFactor, removeWhenSettled: false);
    }

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
