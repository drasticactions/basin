using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs KWin's slideback effect. A window moves through a translation from an offset back to its place with a <see cref="WindowMotion"/> on each axis.
/// </summary>
public sealed class SlideBackEffect
{
    private const string NodeName = "slideback";

    private const double BaseStrength = 0.12;

    private const double BaseSmoothness = 2.5;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<Entry> _entries = [];
    private long _lastNanos;
    private bool _hasLast;

    /// <summary>
    /// Gets a value indicating whether a window still moves.
    /// </summary>
    public bool IsActive => _entries.Count > 0;

    /// <summary>
    /// Starts or retargets the motion of a window. A window that already moves keeps its current offset and velocity.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="fromX">The start horizontal offset, in pixels. It applies only to a new motion.</param>
    /// <param name="fromY">The start vertical offset, in pixels. It applies only to a new motion.</param>
    /// <param name="toX">The target horizontal offset, in pixels.</param>
    /// <param name="toY">The target vertical offset, in pixels.</param>
    /// <param name="durationFactor">The animation factor of the desktop. A larger value slows the motion. Zero or less jumps to the target.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
    public void Move(TransformStack stack, double fromX, double fromY, double toX, double toY, double durationFactor)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        var strength = durationFactor > 0 ? BaseStrength / durationFactor : BaseStrength;
        var smoothness = durationFactor > 0 ? durationFactor * BaseSmoothness : BaseSmoothness;

        var entry = Find(stack);
        if (entry is null)
        {
            entry = new Entry
            {
                Stack = stack,
                Node = stack.Get(NodeName) ?? stack.Add(TransformStack.ZOrder.Transform2D, NodeName),
                X = new WindowMotion(fromX, strength, smoothness),
                Y = new WindowMotion(fromY, strength, smoothness),
            };
            _entries.Add(entry);
        }

        entry.X.Strength = strength;
        entry.Y.Strength = strength;
        entry.X.Smoothness = smoothness;
        entry.Y.Smoothness = smoothness;
        entry.X.SetTarget(toX);
        entry.Y.SetTarget(toY);
        if (durationFactor <= 0)
        {
            entry.X.Finish();
            entry.Y.Finish();
        }

        Apply(entry);
    }

    /// <summary>
    /// Advances every motion by the time since the last tick. A motion that settles on both axes removes its node.
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

            entry.X.Calculate(elapsed);
            entry.Y.Calculate(elapsed);
            var settled = 0;
            if (entry.X.IsSettled())
            {
                entry.X.Finish();
                settled++;
            }

            if (entry.Y.IsSettled())
            {
                entry.Y.Finish();
                settled++;
            }

            Apply(entry);
            if (settled == 2)
            {
                entry.Stack.Remove(NodeName);
                _entries.RemoveAt(i);
            }
        }

        return IsActive;
    }

    /// <summary>
    /// Stops the motion of a window and removes its node.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
    public void Release(TransformStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_entries[i].Stack, stack))
            {
                _entries.RemoveAt(i);
                stack.Remove(NodeName);
            }
        }
    }

    private Entry? Find(TransformStack stack)
    {
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Stack, stack))
            {
                return entry;
            }
        }

        return null;
    }

    private static void Apply(Entry entry)
    {
        if (!entry.Node.IsDestroyed)
        {
            entry.Node.Matrix = RenderTransform.Translation(entry.X.Value, entry.Y.Value);
        }
    }

    private sealed class Entry
    {
        public required TransformStack Stack;
        public required SceneTransform Node;
        public WindowMotion X;
        public WindowMotion Y;
    }
}
