using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Seat;

public sealed class SeatDiagnostics : IInputDiagnostics, IFrameSink
{
    private readonly Seat _seat;
    private readonly BasinServices _services;
    private readonly List<Watcher> _watchers = [];
    private IFrameClock? _clock;
    private InputTraceEvent? _pendingMotion;
    private bool _hooked;
    private bool _motionHooked;

    public SeatDiagnostics(Seat seat, BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(services);
        _seat = seat;
        _services = services;
    }

    public IDisposable WatchInput(bool motion, Action<InputTraceEvent> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var watcher = new Watcher(this, motion, sink);
        _watchers.Add(watcher);
        Rehook();
        return watcher;
    }

    public void BeginFrame(IOutput output, long predictedVblankNanos) => FlushMotion();

    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

    private bool WantsMotion
    {
        get
        {
            foreach (var watcher in _watchers)
            {
                if (watcher.Motion)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void Rehook()
    {
        var pointer = _seat.Pointer;
        if (_watchers.Count > 0 && !_hooked)
        {
            _hooked = true;
            pointer.FocusChanged += OnPointerFocus;
            pointer.Buttoned += OnButton;
            pointer.Axised += OnAxis;
            pointer.GrabChanged += OnGrab;
            _seat.Keyboard.FocusChanged += OnKeyboardFocus;
        }
        else if (_watchers.Count == 0 && _hooked)
        {
            _hooked = false;
            pointer.FocusChanged -= OnPointerFocus;
            pointer.Buttoned -= OnButton;
            pointer.Axised -= OnAxis;
            pointer.GrabChanged -= OnGrab;
            _seat.Keyboard.FocusChanged -= OnKeyboardFocus;
        }

        var motion = _watchers.Count > 0 && WantsMotion;
        if (motion && !_motionHooked)
        {
            _motionHooked = true;
            pointer.Moved += OnMoved;
            _clock = _services.Find<IFrameClock>();
            _clock?.Add(this);
        }
        else if (!motion && _motionHooked)
        {
            _motionHooked = false;
            pointer.Moved -= OnMoved;
            _clock?.Remove(this);
            _clock = null;
            _pendingMotion = null;
        }
    }

    private InputTraceEvent Event(string kind, Surface? surface, uint code = 0, bool pressed = false, double value = 0, string? detail = null)
    {
        var pointer = _seat.Pointer;
        return new InputTraceEvent(kind, surface, pointer.X, pointer.Y, pointer.LayoutX, pointer.LayoutY, code, pressed, value, detail);
    }

    private void Send(in InputTraceEvent trace, bool motion = false)
    {
        AllocationScope.Pause();
        try
        {
            if (!motion)
            {
                FlushMotion();
            }

            foreach (var watcher in _watchers.ToArray())
            {
                if (!motion || watcher.Motion)
                {
                    watcher.Sink(trace);
                }
            }
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private void FlushMotion()
    {
        if (_pendingMotion is not { } motion)
        {
            return;
        }

        _pendingMotion = null;
        Send(motion, motion: true);
    }

    private void OnPointerFocus(Surface? surface) => Send(Event(surface is null ? "leave" : "enter", surface));

    private void OnButton(uint button, bool pressed) => Send(Event("button", _seat.Pointer.Focus, button, pressed));

    private void OnAxis(uint timeMs, PointerAxis axis) =>
        Send(Event("axis", _seat.Pointer.Focus, (uint)axis.Axis, value: axis.Value, detail: axis.Source.ToString().ToLowerInvariant()));

    private void OnGrab(IPointerGrab grab, bool started) =>
        Send(Event(started ? "grab-start" : "grab-end", _seat.Pointer.Focus, detail: grab.GetType().Name));

    private void OnKeyboardFocus(Surface? surface) => Send(Event("keyboard-focus", surface));

    private void OnMoved(uint timeMs, double dx, double dy)
    {
        _pendingMotion = Event("motion", _seat.Pointer.Focus);
        if (_clock is null)
        {
            FlushMotion();
        }
    }

    private sealed class Watcher(SeatDiagnostics owner, bool motion, Action<InputTraceEvent> sink) : IDisposable
    {
        public bool Motion { get; } = motion;

        public Action<InputTraceEvent> Sink { get; } = sink;

        public void Dispose()
        {
            if (owner._watchers.Remove(this))
            {
                owner.Rehook();
            }
        }
    }
}
