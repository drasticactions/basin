using Basin.Capabilities;
using static Basin.Eis.EisLog;

namespace Basin.Eis;

/// <summary>
/// Owns the input capture sessions of one seat. It tests pointer motion against the barriers and grabs the seat while a session captures input.
/// </summary>
public sealed class InputCaptureEngine : IDisposable
{
    private readonly List<InputCaptureSession> _sessions = [];
    private readonly InputCaptureGrab _grab;
    private InputCaptureSession? _active;
    private bool _grabbing;
    private uint _zoneSet;

    /// <summary>
    /// Initializes a new instance of the <see cref="InputCaptureEngine"/> class. It subscribes to layout and keymap changes until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="loop">The compositor loop that the EIS contexts and repeat timers use.</param>
    /// <param name="layout">The output layout that gives barrier geometry and pointer regions.</param>
    /// <param name="seat">The seat whose pointer and keyboard the engine grabs.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public InputCaptureEngine(ICompositorEventLoop loop, OutputLayout layout, Basin.Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(seat);
        Loop = loop;
        Layout = layout;
        Seat = seat;
        _grab = new InputCaptureGrab(this);
        layout.Changed += OnLayoutChanged;
        seat.Keyboard.KeymapChanged += OnKeymapChanged;
    }

    /// <summary>
    /// Gets the compositor loop that the engine and its sessions run on.
    /// </summary>
    public ICompositorEventLoop Loop { get; }

    /// <summary>
    /// Gets the output layout that the barriers are checked against.
    /// </summary>
    public OutputLayout Layout { get; }

    /// <summary>
    /// Gets the seat that the engine grabs during a capture.
    /// </summary>
    public Basin.Seat.Seat Seat { get; }

    /// <summary>
    /// Gets the seat keyboard's keymap, which each session's EIS keyboard device sends.
    /// </summary>
    public IActiveKeymap Keymap => Seat.Keyboard;

    /// <summary>
    /// Gets or sets a value indicating whether an invalid barrier is refused. When it is <see langword="false"/>, an invalid barrier is logged and kept. The default is <see langword="true"/>.
    /// </summary>
    public bool EnforceBarriers { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether a session captures the input now.
    /// </summary>
    public bool IsCaptured => _active is not null;

    /// <summary>
    /// Gets the number of sessions that are not disposed.
    /// </summary>
    public int SessionCount => _sessions.Count;

    /// <summary>
    /// Gets the zone set serial. It starts at 0 and increases by one on each layout change.
    /// </summary>
    public uint ZoneSet => _zoneSet;

    /// <summary>
    /// Occurs when a session is released with a cursor position. The arguments are the layout x and y that the compositor must move the pointer to.
    /// </summary>
    public event Action<double, double>? WarpRequested;

    internal InputCaptureSession? Active => _active;

    /// <summary>
    /// Creates a session with its own EIS sender context. The session starts in the created state, with no barriers.
    /// </summary>
    /// <param name="front">The front that receives the session's state changes.</param>
    /// <param name="handle">The session handle, used in log lines.</param>
    /// <returns>The new session. The caller disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="front"/> or <paramref name="handle"/> is <see langword="null"/>.</exception>
    public InputCaptureSession CreateSession(IInputCaptureFront front, string handle)
    {
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(handle);
        var eis = new EisSender(Loop, Layout, Seat.Keyboard);
        var session = new InputCaptureSession(this, front, handle, eis);
        _sessions.Add(session);
        return session;
    }

    /// <summary>
    /// Gives one pointer motion to the engine. When no session captures, the first enabled session with a crossed barrier activates and the seat is grabbed.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds. It is not used.</param>
    /// <param name="layoutX">The layout x after the motion.</param>
    /// <param name="layoutY">The layout y after the motion.</param>
    /// <param name="dx">The relative x motion.</param>
    /// <param name="dy">The relative y motion.</param>
    /// <returns><see langword="true"/> when a session captured the motion and the compositor must not move its own pointer.</returns>
    public bool NotifyMotion(uint timeMs, double layoutX, double layoutY, double dx, double dy)
    {
        _ = timeMs;
        if (_active is null)
        {
            var fromX = layoutX - dx;
            var fromY = layoutY - dy;
            foreach (var session in _sessions)
            {
                if (!session.IsEnabled)
                {
                    continue;
                }

                foreach (var barrier in session.Barriers)
                {
                    if (!InputCaptureBarriers.Crosses(in barrier, fromX, fromY, layoutX, layoutY))
                    {
                        continue;
                    }

                    if (session.Activate(layoutX, layoutY, barrier.Id))
                    {
                        _active = session;
                        BeginGrab();
                    }

                    break;
                }

                if (_active is not null)
                {
                    break;
                }
            }
        }

        if (_active is not { } active)
        {
            return false;
        }

        active.Motion(dx, dy);
        return true;
    }

    /// <summary>
    /// Ends the active capture without a client request, for example on an escape chord. It deactivates and disables the active session and ends the grab.
    /// </summary>
    public void ForceRelease()
    {
        if (_active is { } active)
        {
            active.Deactivate();
            active.Disable();
        }

        Released(null);
    }

    /// <summary>
    /// Disposes every session, ends the grab and stops the layout and keymap subscriptions.
    /// </summary>
    public void Dispose()
    {
        Layout.Changed -= OnLayoutChanged;
        Seat.Keyboard.KeymapChanged -= OnKeymapChanged;
        for (var i = _sessions.Count - 1; i >= 0; i--)
        {
            _sessions[i].Dispose();
        }

        _sessions.Clear();
        Released(null);
    }

    internal void Released(InputCaptureSession? session)
    {
        if (session is not null && !ReferenceEquals(_active, session))
        {
            return;
        }

        _active = null;
        EndGrab();
    }

    internal void RequestWarp(double x, double y) => WarpRequested?.Invoke(x, y);

    internal void Forget(InputCaptureSession session)
    {
        _sessions.Remove(session);
        if (ReferenceEquals(_active, session))
        {
            Released(session);
        }
    }

    private void BeginGrab()
    {
        if (_grabbing)
        {
            return;
        }

        _grabbing = true;
        Seat.Pointer.StartGrab(_grab);
        Seat.Keyboard.StartGrab(_grab);
    }

    private void EndGrab()
    {
        if (!_grabbing)
        {
            return;
        }

        _grabbing = false;
        Seat.Pointer.EndGrab(_grab);
        Seat.Keyboard.EndGrab(_grab);
    }

    private void OnLayoutChanged()
    {
        _zoneSet++;
        Log.Debug($"capture: layout changed, zone set {_zoneSet}");
        foreach (var session in _sessions.ToArray())
        {
            session.LayoutChanged();
        }
    }

    private void OnKeymapChanged()
    {
        foreach (var session in _sessions)
        {
            session.KeymapChanged();
        }
    }
}
