using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs KWin's highlight window effect. It fades the windows that are not highlighted toward a ghost opacity, for example while a taskbar entry is hovered.
/// </summary>
public sealed class HighlightWindowEffect
{
    /// <summary>
    /// The default fade duration, in milliseconds.
    /// </summary>
    public const double DefaultMillis = 150;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<TransformStack, Entry> _entries = [];
    private readonly List<TransformStack> _finished = [];
    private readonly double _ghostOpacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="HighlightWindowEffect"/> class.
    /// </summary>
    /// <param name="ghostOpacity">The opacity of a window that is not highlighted, clamped between 0 and 1.</param>
    public HighlightWindowEffect(double ghostOpacity = 0.0) =>
        _ghostOpacity = Math.Clamp(ghostOpacity, 0, 1);

    /// <summary>
    /// Gets the opacity of a window that is not highlighted.
    /// </summary>
    public double GhostOpacity => _ghostOpacity;

    /// <summary>
    /// Gets a value indicating whether a window has a highlight node.
    /// </summary>
    public bool IsActive => _entries.Count > 0;

    /// <summary>
    /// Fades a window toward full opacity or toward the ghost opacity, from its current opacity.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="highlighted"><see langword="true"/> for full opacity, <see langword="false"/> for the ghost opacity.</param>
    /// <param name="now">The tick that the fade starts at.</param>
    /// <param name="duration">The length of the fade. A disabled duration changes the opacity at once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
    public void Highlight(TransformStack stack, bool highlighted, in FrameTick now, AnimationDuration duration)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        var target = highlighted ? 1.0 : _ghostOpacity;
        if (!_entries.TryGetValue(stack, out var entry))
        {
            entry = new Entry { Node = stack.Get("highlight") ?? stack.Add(TransformStack.ZOrder.Transform2D, "highlight"), Current = 1.0 };
            _entries[stack] = entry;
        }

        if (Math.Abs(entry.Target - target) < 1e-9)
        {
            return;
        }

        entry.From = entry.Current;
        entry.Target = target;
        if (duration.IsDisabled)
        {
            entry.Current = target;
            Apply(entry);
            return;
        }

        entry.Timeline.Easing = EasingCurve.Linear;
        entry.Timeline.Start(now, duration.Nanos);
        entry.Animating = true;
        Apply(entry);
    }

    /// <summary>
    /// Removes the highlight node of a window at once, which restores full opacity.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is <see langword="null"/>.</exception>
    public void Clear(TransformStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _thread.Assert();
        if (_entries.Remove(stack))
        {
            stack.Remove("highlight");
        }
    }

    /// <summary>
    /// Advances every fade. A window that is back at full opacity loses its node.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while a fade runs. A window that stays at the ghost opacity does not count.</returns>
    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        var running = false;
        _finished.Clear();
        foreach (var (stack, entry) in _entries)
        {
            if (entry.Node.IsDestroyed)
            {
                _finished.Add(stack);
                continue;
            }

            if (!entry.Animating)
            {
                if (Math.Abs(entry.Current - 1.0) < 1e-9)
                {
                    _finished.Add(stack);
                }

                continue;
            }

            var progress = entry.Timeline.Progress(tick);
            entry.Current = entry.From + ((entry.Target - entry.From) * progress);
            Apply(entry);
            if (entry.Timeline.Running(tick))
            {
                running = true;
            }
            else
            {
                entry.Current = entry.Target;
                entry.Animating = false;
                Apply(entry);
                if (Math.Abs(entry.Current - 1.0) < 1e-9)
                {
                    _finished.Add(stack);
                }
            }
        }

        foreach (var stack in _finished)
        {
            _entries.Remove(stack);
            stack.Remove("highlight");
        }

        _finished.Clear();
        return running;
    }

    private static void Apply(Entry entry)
    {
        if (!entry.Node.IsDestroyed)
        {
            entry.Node.Alpha = (float)Math.Clamp(entry.Current, 0, 1);
        }
    }

    private sealed class Entry
    {
        public required SceneTransform Node;
        public EffectTimeline Timeline;
        public double From = 1.0;
        public double Target = 1.0;
        public double Current = 1.0;
        public bool Animating;
    }
}
