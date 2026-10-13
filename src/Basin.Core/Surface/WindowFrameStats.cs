using Basin.Capabilities;

namespace Basin;

public sealed class WindowFrameStats : IFrameStats, IDisposable
{
    public const int Capacity = 120;

    private const long Window = 1_000_000_000;

    private readonly BasinServices _services;
    private readonly Dictionary<Surface, Tracker> _trackers = [];
    private readonly List<IOutput> _hooked = [];
    private readonly Dictionary<IOutput, Action> _frameHandlers = [];
    private readonly Dictionary<IOutput, Action<ulong, uint, ulong>> _presentHandlers = [];
    private OutputLayout? _layout;
    private CompositorGlobal? _compositor;
    private int _holders;

    public WindowFrameStats(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public bool IsActive => _holders > 0;

    public void Acquire()
    {
        if (_holders++ > 0)
        {
            return;
        }

        _layout ??= _services.Find<OutputLayout>();
        _compositor ??= _services.Find<CompositorGlobal>();
        if (_layout is { } layout)
        {
            layout.Changed += Rehook;
        }

        Rehook();
    }

    public void Release()
    {
        if (_holders == 0 || --_holders > 0)
        {
            return;
        }

        if (_layout is { } layout)
        {
            layout.Changed -= Rehook;
        }

        Unhook();
        foreach (var (surface, tracker) in _trackers)
        {
            surface.Committed -= tracker.OnCommitted;
            surface.Destroyed -= tracker.OnDestroyed;
        }

        _trackers.Clear();
    }

    public void Dispose()
    {
        if (_holders > 0)
        {
            _holders = 1;
            Release();
        }
    }

    public bool TryGet(Surface surface, long nowNanos, out FrameStatsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!_trackers.TryGetValue(surface, out var tracker))
        {
            snapshot = default;
            return false;
        }

        snapshot = tracker.Snapshot(nowNanos);
        return true;
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
        if (_layout is not { } layout)
        {
            return;
        }

        foreach (var (output, _) in layout.Outputs)
        {
            var target = output;
            if (output is IPresentingOutput presenting)
            {
                Action<ulong, uint, ulong> present = (time, _, _) => OnPresented(target, (long)time);
                presenting.PresentedOnScreen += present;
                _presentHandlers[output] = present;
            }
            else
            {
                Action frame = () => OnPresented(target, MonotonicClock.Nanos);
                output.Frame += frame;
                _frameHandlers[output] = frame;
            }

            _hooked.Add(output);
        }
    }

    private void OnPresented(IOutput output, long timeNanos)
    {
        if (_compositor?.PresentedSets.Find(output) is not { } set)
        {
            return;
        }

        for (var i = 0; i < set.Count; i++)
        {
            var surface = set[i];
            if (!set.IsMain(surface))
            {
                continue;
            }

            if (!_trackers.TryGetValue(surface, out var tracker))
            {
                tracker = new Tracker(this, surface);
                _trackers[surface] = tracker;
                surface.Committed += tracker.OnCommitted;
                surface.Destroyed += tracker.OnDestroyed;
            }

            tracker.OnPresented(output, timeNanos);
        }
    }

    private sealed class Tracker(WindowFrameStats owner, Surface surface)
    {
        private readonly long[] _commits = new long[Capacity];
        private readonly long[] _presents = new long[Capacity];
        private readonly long[] _latencies = new long[Capacity];
        private int _commitNext;
        private int _commitCount;
        private int _presentNext;
        private int _presentCount;
        private long _lastCommit;
        private long _commitSeq;
        private long _presentedSeq;
        private IOutput? _output;

        public void OnCommitted()
        {
            var now = MonotonicClock.Nanos;
            _commits[_commitNext] = now;
            _commitNext = (_commitNext + 1) % Capacity;
            _commitCount = Math.Min(_commitCount + 1, Capacity);
            _lastCommit = now;
            _commitSeq++;
        }

        public void OnDestroyed()
        {
            surface.Committed -= OnCommitted;
            surface.Destroyed -= OnDestroyed;
            owner._trackers.Remove(surface);
        }

        public void OnPresented(IOutput output, long timeNanos)
        {
            _output = output;
            if (_commitSeq == _presentedSeq)
            {
                return;
            }

            _presentedSeq = _commitSeq;
            _presents[_presentNext] = timeNanos;
            _latencies[_presentNext] = Math.Max(0, timeNanos - _lastCommit);
            _presentNext = (_presentNext + 1) % Capacity;
            _presentCount = Math.Min(_presentCount + 1, Capacity);
        }

        public FrameStatsSnapshot Snapshot(long nowNanos)
        {
            var since = nowNanos - Window;
            var commits = 0;
            for (var i = 0; i < _commitCount; i++)
            {
                if (_commits[i] >= since)
                {
                    commits++;
                }
            }

            Span<long> intervals = stackalloc long[Capacity];
            Span<long> latencies = stackalloc long[Capacity];
            var presents = 0;
            var gaps = 0;
            for (var age = 0; age < _presentCount; age++)
            {
                var index = (_presentNext - 1 - age + Capacity) % Capacity;
                if (_presents[index] < since)
                {
                    break;
                }

                latencies[presents++] = _latencies[index];
                if (age + 1 < _presentCount)
                {
                    var previous = (index - 1 + Capacity) % Capacity;
                    intervals[gaps++] = _presents[index] - _presents[previous];
                }
            }

            var (meanInterval, p99Interval) = Summary(intervals[..gaps]);
            var (meanLatency, p99Latency) = Summary(latencies[..presents]);
            return new FrameStatsSnapshot(
                commits, presents, meanInterval, p99Interval, meanLatency, p99Latency,
                _output, _output?.CurrentMode.RefreshMilliHz ?? 0);
        }

        private static (double Mean, double P99) Summary(Span<long> values)
        {
            if (values.Length == 0)
            {
                return (0, 0);
            }

            var total = 0L;
            foreach (var value in values)
            {
                total += value;
            }

            values.Sort();
            var p99 = values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * 0.99) - 1)];
            return (total / (double)values.Length / 1e6, p99 / 1e6);
        }
    }
}
