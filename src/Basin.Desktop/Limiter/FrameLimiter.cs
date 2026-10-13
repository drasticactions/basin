using Basin.Capabilities;

namespace Basin.Desktop;

public sealed class FrameLimiter : IFrameLimits, IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly OutputLayout _layout;
    private readonly IEventSource _timer;
    private readonly List<Entry> _entries = [];
    private readonly List<IOutput> _hooked = [];
    private readonly Dictionary<IOutput, Action> _frameHandlers = [];
    private readonly Dictionary<IOutput, Action<ulong, uint, ulong>> _presentHandlers = [];
    private readonly Dictionary<IOutput, (ulong Sequence, long TimeNanos)> _vblanks = [];
    private readonly Dictionary<IOutput, ulong> _previousVblank = [];

    public FrameLimiter(ICompositorEventLoop loop, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(layout);
        _loop = loop;
        _layout = layout;
        _timer = loop.AddTimer(OnTimer);
        _layout.Changed += Rehook;
        Rehook();
    }

    public event Action<Surface>? Changed;

    public int Count => _entries.Count;

    public int LimitOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var index = IndexOf(surface);
        return index < 0 ? 0 : _entries[index].Fps;
    }

    public void SetLimit(Surface surface, int fps)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentOutOfRangeException.ThrowIfNegative(fps);
        var index = IndexOf(surface);
        if (fps == 0)
        {
            if (index < 0)
            {
                return;
            }

            _entries.RemoveAt(index);
            surface.PacingHeld = false;
            surface.PacingIntervalNanos = 0;
            surface.Destroyed -= _entriesCleanup[surface];
            _entriesCleanup.Remove(surface);
            Changed?.Invoke(surface);
            return;
        }

        if (index >= 0)
        {
            if (_entries[index].Fps == fps)
            {
                return;
            }

            _entries[index] = new Entry(surface, fps);
        }
        else
        {
            _entries.Add(new Entry(surface, fps));
            Action cleanup = () => SetLimit(surface, 0);
            _entriesCleanup[surface] = cleanup;
            surface.Destroyed += cleanup;
        }

        surface.PacingHeld = false;
        Changed?.Invoke(surface);
        ArmTimer();
    }

    private readonly Dictionary<Surface, Action> _entriesCleanup = [];

    public void Dispose()
    {
        _layout.Changed -= Rehook;
        Unhook();
        _timer.Remove();
        foreach (var entry in _entries)
        {
            entry.Surface.PacingHeld = false;
            entry.Surface.PacingIntervalNanos = 0;
        }

        foreach (var (surface, cleanup) in _entriesCleanup)
        {
            surface.Destroyed -= cleanup;
        }

        _entries.Clear();
        _entriesCleanup.Clear();
    }

    internal void Tick(IOutput output, ulong sequence, long nowNanos)
    {
        var refresh = output.CurrentMode.RefreshMilliHz;
        var interval = (long)output.CurrentMode.RefreshIntervalNanoseconds;
        var variable = output.AdaptiveSync;
        var previous = _previousVblank.GetValueOrDefault(output);
        _previousVblank[output] = sequence;
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var surface = entry.Surface;
            var main = surface.MainOutput;
            if (main is not null && !ReferenceEquals(main, output))
            {
                continue;
            }

            var cycle = FrameLimitCadence.LimitedCycleNanos(interval, refresh, entry.Fps, variable);
            surface.PacingIntervalNanos = (uint)Math.Min(cycle, uint.MaxValue);
            if (surface.PacingUses != entry.UsesSeen)
            {
                entry.UsesSeen = surface.PacingUses;
                entry.Unlocked = false;
                entry.Anchor = FrameLimitCadence.NextAnchor(surface.LastPacingUseNanos, entry.Anchor, cycle, interval);
            }

            var opens = variable
                ? FrameLimitCadence.VariableOpens(nowNanos, entry.Anchor, cycle)
                : FrameLimitCadence.FixedOpens(sequence, previous, FrameLimitCadence.Divisor(refresh, entry.Fps));
            if (opens && !entry.Unlocked)
            {
                entry.Unlocked = true;
                output.RequestRepaint();
            }

            surface.PacingHeld = !entry.Unlocked;
            _entries[i] = entry;
        }

        ArmTimer();
    }

    private int IndexOf(Surface surface)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Surface, surface))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnPresented(IOutput output, ulong timeNanos, ulong sequence)
    {
        _vblanks[output] = (sequence, (long)timeNanos);
        Tick(output, sequence, (long)timeNanos);
    }

    private void OnFrame(IOutput output)
    {
        if (output is IPresentingOutput || _entries.Count == 0)
        {
            return;
        }

        var (sequence, _) = _vblanks.GetValueOrDefault(output);
        sequence++;
        var now = MonotonicClock.Nanos;
        _vblanks[output] = (sequence, now);
        Tick(output, sequence, now);
    }

    private void OnTimer()
    {
        var now = MonotonicClock.Nanos;
        foreach (var output in _hooked)
        {
            var interval = (long)output.CurrentMode.RefreshIntervalNanoseconds;
            if (output.AdaptiveSync && _vblanks.TryGetValue(output, out var variable))
            {
                Tick(output, variable.Sequence, now);
                continue;
            }

            if (interval <= 0 || !_vblanks.TryGetValue(output, out var last) || now - last.TimeNanos < interval + (interval >> 1))
            {
                continue;
            }

            var elapsed = (now - last.TimeNanos) / interval;
            var sequence = last.Sequence + (ulong)elapsed;
            _vblanks[output] = (sequence, last.TimeNanos + (elapsed * interval));
            Tick(output, sequence, now);
        }

        ArmTimer();
    }

    private void ArmTimer()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var now = MonotonicClock.Nanos;
        var shortest = long.MaxValue;
        foreach (var entry in _entries)
        {
            var due = entry.Anchor + (1_000_000_000L / Math.Max(1, entry.Fps));
            var wait = due > now ? due - now : 1_000_000_000L / Math.Max(1, entry.Fps);
            shortest = Math.Min(shortest, wait);
        }

        _timer.UpdateTimer((int)Math.Max(1, (shortest + 999_999) / 1_000_000));
    }

    private void Unhook()
    {
        foreach (var output in _hooked)
        {
            if (_frameHandlers.Remove(output, out var frame))
            {
                output.Frame -= frame;
            }

            if (output is IPresentingOutput presenting && _presentHandlers.Remove(output, out var present))
            {
                presenting.PresentedOnScreen -= present;
            }
        }

        _hooked.Clear();
    }

    private void Rehook()
    {
        Unhook();
        foreach (var (output, _) in _layout.Outputs)
        {
            var target = output;
            Action frame = () => OnFrame(target);
            output.Frame += frame;
            _frameHandlers[output] = frame;
            if (output is IPresentingOutput presenting)
            {
                Action<ulong, uint, ulong> present = (time, _, sequence) => OnPresented(target, time, sequence);
                presenting.PresentedOnScreen += present;
                _presentHandlers[output] = present;
            }

            _vblanks.TryAdd(output, (0, MonotonicClock.Nanos));
            _hooked.Add(output);
        }
    }

    private struct Entry(Surface surface, int fps)
    {
        public Surface Surface = surface;
        public int Fps = fps;
        public long UsesSeen = surface.PacingUses;
        public long Anchor;
        public bool Unlocked = true;
    }
}
