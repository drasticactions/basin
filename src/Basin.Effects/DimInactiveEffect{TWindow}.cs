using Basin.Diagnostics;

namespace Basin.Effects;

/// <summary>
/// Dims the inactive windows by lowering their brightness and saturation. The consumer adds its windows with their facts and reports focus changes. The effect decides which windows dim and fades each one on its own.
/// </summary>
/// <typeparam name="TWindow">The window type of the consumer. The effect compares windows by reference.</typeparam>
public sealed class DimInactiveEffect<TWindow>
    where TWindow : class
{
    private const double Settled = 1e-6;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly DimInactiveOptions _options;
    private readonly double _strength;
    private readonly Dictionary<TWindow, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private TWindow? _active;
    private ulong _activeGroup;
    private Fade _fullScreen = Fade.At(1);
    private long _anchorNanos;
    private bool _anchored;

    /// <summary>
    /// Initializes a new instance of the <see cref="DimInactiveEffect{TWindow}"/> class.
    /// </summary>
    /// <param name="options">The settings, or <see langword="null"/> for <see cref="DimInactiveOptions()"/>.</param>
    public DimInactiveEffect(DimInactiveOptions? options = null)
    {
        _options = options ?? new DimInactiveOptions();
        _strength = Math.Clamp(_options.Strength / 100.0, 0.1, 0.9);
    }

    /// <summary>
    /// Gets the settings that the effect was created with.
    /// </summary>
    public DimInactiveOptions Options => _options;

    /// <summary>
    /// Gets the strength as a fraction, clamped to the range from 0.1 to 0.9.
    /// </summary>
    public double Strength => _strength;

    /// <summary>
    /// Gets or sets the object that receives the dim of each window. Set it before the first <see cref="Add"/>.
    /// </summary>
    public IDimInactiveTarget<TWindow>? Target { get; set; }

    /// <summary>
    /// Gets or sets the factor that multiplies every fade duration. Zero turns the fades off, and each change then shows at once.
    /// </summary>
    public double DurationFactor { get; set; } = 1.0;

    /// <summary>
    /// Gets the active window that the rules keep undimmed, or <see langword="null"/>. It is <see langword="null"/> also when the focused window is one that cannot dim.
    /// </summary>
    public TWindow? Active => _active;

    /// <summary>
    /// Gets a value indicating whether a fade is running and <see cref="Step"/> must be called.
    /// </summary>
    public bool IsAnimating
    {
        get
        {
            if (_fullScreen.Running)
            {
                return true;
            }

            foreach (var entry in _entries.Values)
            {
                if (entry.Amount.Running)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Starts to track a window. The window takes its dim at once, without a fade, until the next <see cref="Step"/>.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="facts">The facts of the window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The effect already tracks the window.</exception>
    public void Add(TWindow window, in DimInactiveWindow facts)
    {
        ArgumentNullException.ThrowIfNull(window);
        _thread.Assert();
        if (_entries.ContainsKey(window))
        {
            throw new InvalidOperationException("the effect already tracks this window");
        }

        var entry = new Entry(window, facts) { Fresh = true };
        _entries.Add(window, entry);
        entry.Amount = Fade.At(StaticAmount(entry));
        Push(entry);
    }

    /// <summary>
    /// Replaces the facts of a window. A window whose rules change takes its new dim at once.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="facts">The new facts.</param>
    /// <returns><see langword="false"/> when the effect does not track the window.</returns>
    public bool Update(TWindow window, in DimInactiveWindow facts)
    {
        _thread.Assert();
        if (window is null || !_entries.TryGetValue(window, out var entry))
        {
            return false;
        }

        entry.Facts = facts;
        if (ReferenceEquals(window, _active))
        {
            _active = null;
            _active = CanDim(entry) ? window : null;
        }

        Settle();
        return true;
    }

    /// <summary>
    /// Gets the facts that a tracked window was added or updated with.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="facts">The facts, or the default value when the window is not tracked.</param>
    /// <returns><see langword="false"/> when the effect does not track the window.</returns>
    public bool TryGetFacts(TWindow window, out DimInactiveWindow facts)
    {
        _thread.Assert();
        if (window is not null && _entries.TryGetValue(window, out var entry))
        {
            facts = entry.Facts;
            return true;
        }

        facts = default;
        return false;
    }

    /// <summary>
    /// Makes a window the active one, with fades that start at the next <see cref="Step"/>. The previous active window and its group fade to the dim, and the new one and its group fade to full brightness. A <see langword="null"/> window keeps the last active one, so focus on nothing changes nothing.
    /// </summary>
    /// <param name="window">The focused window, or <see langword="null"/>. A window that the effect does not track counts as one that cannot dim.</param>
    public void Activate(TWindow? window)
    {
        _anchored = false;
        ActivateCore(window);
    }

    /// <summary>
    /// Makes a window the active one, with fades anchored exactly at a tick. The rules are those of <see cref="Activate(TWindow)"/>.
    /// </summary>
    /// <param name="window">The focused window, or <see langword="null"/>.</param>
    /// <param name="now">The tick that anchors the fades.</param>
    public void Activate(TWindow? window, in FrameTick now)
    {
        _anchorNanos = now.TargetPresentNanos;
        _anchored = true;
        ActivateCore(window);
    }

    private void ActivateCore(TWindow? window)
    {
        _thread.Assert();
        if (window is null || ReferenceEquals(window, _active))
        {
            return;
        }

        _entries.TryGetValue(window, out var entry);
        var activeEntry = _active is null ? null : _entries.GetValueOrDefault(_active);
        if (_options.DimByGroup && entry is not null && activeEntry is not null && SameGroup(activeEntry.Facts, entry.Facts))
        {
            _active = window;
            return;
        }

        if (_options.DimByGroup && entry is not null && entry.Facts.Group != 0 && entry.Facts.Group == _activeGroup)
        {
            _active = window;
            return;
        }

        var previous = activeEntry;
        _active = entry is not null && CanDim(entry) ? window : null;
        _activeGroup = _options.DimByGroup && entry is not null && _active is not null ? entry.Facts.Group : 0;
        if (previous is not null)
        {
            ScheduleGroup(previous, _options.DeactivateMillis);
        }

        if (_active is not null)
        {
            ScheduleGroup(entry!, _options.ActivateMillis);
        }

        Settle();
    }

    /// <summary>
    /// Freezes the dim of a window that closes. The effect does not change it again until <see cref="Remove"/>.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns><see langword="false"/> when the effect does not track the window.</returns>
    public bool Close(TWindow window)
    {
        _thread.Assert();
        if (window is null || !_entries.TryGetValue(window, out var entry))
        {
            return false;
        }

        entry.Closed = true;
        entry.Amount = Fade.At(entry.Amount.Value);
        if (ReferenceEquals(window, _active))
        {
            _active = null;
        }

        return true;
    }

    /// <summary>
    /// Stops to track a window. The effect does not call <see cref="Target"/> for it again.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns><see langword="false"/> when the effect does not track the window.</returns>
    public bool Remove(TWindow window)
    {
        _thread.Assert();
        if (window is null || !_entries.Remove(window))
        {
            return false;
        }

        if (ReferenceEquals(window, _active))
        {
            _active = null;
        }

        return true;
    }

    /// <summary>
    /// Starts or ends a full-screen effect, such as an overview, with a fade that starts at the next <see cref="Step"/>. While one runs, every dim fades away.
    /// </summary>
    /// <param name="active">Whether a full-screen effect runs.</param>
    public void SetFullScreenEffect(bool active)
    {
        _anchored = false;
        SetFullScreenEffectCore(active);
    }

    /// <summary>
    /// Starts or ends a full-screen effect, with a fade anchored exactly at a tick.
    /// </summary>
    /// <param name="active">Whether a full-screen effect runs.</param>
    /// <param name="now">The tick that anchors the fade.</param>
    public void SetFullScreenEffect(bool active, in FrameTick now)
    {
        _anchorNanos = now.TargetPresentNanos;
        _anchored = true;
        SetFullScreenEffectCore(active);
    }

    private void SetFullScreenEffectCore(bool active)
    {
        _thread.Assert();
        _fullScreen = _fullScreen.To(active ? 0 : 1, _anchorNanos, _anchored, Duration(_options.FullScreenMillis), EasingCurve.Linear);
        Settle();
    }

    /// <summary>
    /// Gets a value indicating whether the rules dim a window now, without its fade.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns><see langword="false"/> when the window is active, cannot dim, or is not tracked.</returns>
    public bool IsDimmed(TWindow window)
    {
        _thread.Assert();
        return window is not null && _entries.TryGetValue(window, out var entry) && StaticAmount(entry) > 0.5;
    }

    /// <summary>
    /// Gets the brightness and saturation factor of a window now.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>A value from 1 for no dim down to 1 minus <see cref="Strength"/>. It is 1 when the window is not tracked.</returns>
    public double BrightnessOf(TWindow window)
    {
        _thread.Assert();
        return window is not null && _entries.TryGetValue(window, out var entry) ? Brightness(entry) : 1.0;
    }

    /// <summary>
    /// Advances the fades and applies the new dim of each window that changed.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while a fade needs more frames.</returns>
    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        var running = _fullScreen.Advance(tick);
        foreach (var entry in _entries.Values)
        {
            running |= entry.Advance(tick);
            entry.Fresh = false;
        }

        Push();
        return running;
    }

    private static bool SameGroup(in DimInactiveWindow a, in DimInactiveWindow b) => a.Group != 0 && a.Group == b.Group;

    private bool CanDim(Entry entry)
    {
        var facts = entry.Facts;
        return facts.Kind switch
        {
            DimInactiveWindowKind.Popup or DimInactiveWindowKind.InputMethod or DimInactiveWindowKind.Unmanaged => false,
            DimInactiveWindowKind.Panel when !_options.DimPanels => false,
            DimInactiveWindowKind.Desktop when !_options.DimDesktop => false,
            _ when facts.KeepAbove && !_options.DimKeepAbove => false,
            _ when facts.FullScreen && !_options.DimFullScreen => false,
            _ => true,
        };
    }

    private double StaticAmount(Entry entry)
    {
        if (entry.Closed)
        {
            return entry.Amount.Value;
        }

        if (ReferenceEquals(entry.Window, _active))
        {
            return 0;
        }

        if (_options.DimByGroup && _active is not null && _entries.TryGetValue(_active, out var active) && SameGroup(active.Facts, entry.Facts))
        {
            return 0;
        }

        return CanDim(entry) ? 1 : 0;
    }

    private void ScheduleGroup(Entry entry, double millis)
    {
        if (!_options.DimByGroup || entry.Facts.Group == 0)
        {
            Schedule(entry, millis);
            return;
        }

        var group = entry.Facts.Group;
        foreach (var member in _entries.Values)
        {
            if (member.Facts.Group == group)
            {
                Schedule(member, millis);
            }
        }
    }

    private void Schedule(Entry entry, double millis)
    {
        if (entry.Closed)
        {
            return;
        }

        var target = StaticAmount(entry);
        var duration = entry.Fresh ? AnimationDuration.Zero : Duration(millis);
        entry.Amount = entry.Amount.To(target, _anchorNanos, _anchored, duration, EasingCurve.InOutSine);
    }

    private void Settle()
    {
        foreach (var entry in _entries.Values)
        {
            if (!entry.Amount.Running && !entry.Closed)
            {
                entry.Amount = Fade.At(StaticAmount(entry));
            }
        }

        Push();
    }

    private AnimationDuration Duration(double millis) => new(millis, DurationFactor);

    private double Brightness(Entry entry) => 1.0 - (_strength * entry.Amount.Value * _fullScreen.Value);

    private void Push()
    {
        foreach (var entry in _entries.Values)
        {
            Push(entry);
        }
    }

    private void Push(Entry entry)
    {
        var brightness = Brightness(entry);
        if (Math.Abs(brightness - entry.Applied) <= Settled)
        {
            return;
        }

        entry.Applied = brightness;
        Target?.Apply(entry.Window, (float)brightness, (float)brightness);
    }

    private struct Fade
    {
        private double _from;
        private double _to;
        private long _startNanos;
        private long _durationNanos;
        private EasingCurve _easing;
        private bool _anchorPending;

        public double Value { get; private set; }

        public bool Running { get; private set; }

        public static Fade At(double value) => new() { Value = value, _from = value, _to = value };

        public readonly Fade To(double target, long startNanos, bool anchored, AnimationDuration duration, EasingCurve easing)
        {
            var distance = Math.Abs(target - Value);
            if (distance <= Settled || duration.IsDisabled)
            {
                return At(target);
            }

            return new Fade
            {
                Value = Value,
                Running = true,
                _from = Value,
                _to = target,
                _startNanos = startNanos,
                _anchorPending = !anchored,
                _durationNanos = Math.Max(1, (long)(duration.Nanos * distance)),
                _easing = easing,
            };
        }

        public bool Advance(in FrameTick tick)
        {
            if (!Running)
            {
                return false;
            }

            if (_anchorPending)
            {
                _startNanos = tick.TargetPresentNanos - Math.Max(0, tick.RefreshIntervalNanos);
                _anchorPending = false;
            }

            var raw = Math.Clamp((tick.TargetPresentNanos - _startNanos) / (double)_durationNanos, 0, 1);
            Value = _from + ((_to - _from) * _easing.Apply(raw));
            if (raw < 1)
            {
                return true;
            }

            Value = _to;
            Running = false;
            return true;
        }
    }

    private sealed class Entry(TWindow window, DimInactiveWindow facts)
    {
        public TWindow Window { get; } = window;

        public DimInactiveWindow Facts { get; set; } = facts;

        public Fade Amount { get; set; }

        public double Applied { get; set; } = 1.0;

        public bool Fresh { get; set; }

        public bool Closed { get; set; }

        public bool Advance(in FrameTick tick)
        {
            var amount = Amount;
            var running = amount.Advance(tick);
            Amount = amount;
            return running;
        }
    }
}
