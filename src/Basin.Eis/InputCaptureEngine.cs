using Basin.Capabilities;
using static Basin.Eis.EisLog;

namespace Basin.Eis;

public sealed class InputCaptureEngine : IDisposable
{
    private readonly List<InputCaptureSession> _sessions = [];
    private readonly InputCaptureGrab _grab;
    private InputCaptureSession? _active;
    private bool _grabbing;
    private uint _zoneSet;

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

    public ICompositorEventLoop Loop { get; }

    public OutputLayout Layout { get; }

    public Basin.Seat.Seat Seat { get; }

    public IActiveKeymap Keymap => Seat.Keyboard;

    public bool EnforceBarriers { get; set; } = true;

    public bool IsCaptured => _active is not null;

    public int SessionCount => _sessions.Count;

    public uint ZoneSet => _zoneSet;

    public event Action<double, double>? WarpRequested;

    internal InputCaptureSession? Active => _active;

    public InputCaptureSession CreateSession(IInputCaptureFront front, string handle)
    {
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(handle);
        var eis = new EisSender(Loop, Layout, Seat.Keyboard);
        var session = new InputCaptureSession(this, front, handle, eis);
        _sessions.Add(session);
        return session;
    }

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

    public void ForceRelease()
    {
        if (_active is { } active)
        {
            active.Deactivate();
            active.Disable();
        }

        Released(null);
    }

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
        Log.Debug($"layout changed, zone set {_zoneSet}");
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
