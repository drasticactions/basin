using System.Runtime.InteropServices;
using Basin.Hypr.InputCapture.Protocol;
using Wayland.Server;
using static Basin.Hypr.InputCapture.InputCaptureLog;

namespace Basin.Hypr.InputCapture;

public sealed class HyprlandInputCaptureManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly List<InputCaptureSession> _sessions = [];
    private readonly InputCaptureGrab _grab;
    private InputCaptureSession? _active;
    private bool _grabbing;

    public HyprlandInputCaptureManager(
        WlServerDisplay display,
        ICompositorEventLoop loop,
        OutputLayout layout,
        Basin.Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(seat);
        Loop = loop;
        Layout = layout;
        Seat = seat;
        _grab = new InputCaptureGrab(this);
        _global = display.CreateGlobal(HyprlandInputCaptureManagerV1.Interface, Version, OnBind);
        layout.Changed += OnLayoutChanged;
        seat.Keyboard.KeymapChanged += OnKeymapChanged;
    }

    public ICompositorEventLoop Loop { get; }

    public OutputLayout Layout { get; }

    public Basin.Seat.Seat Seat { get; }

    public bool EnforceBarriers { get; set; } = true;

    public bool IsCaptured => _active is not null;

    public int SessionCount => _sessions.Count;

    public event Action<double, double>? WarpRequested;

    internal InputCaptureSession? Active => _active;

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
        _global.Dispose();
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

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandInputCaptureManagerV1Resource(client, version, id);
        manager.CreateSession += (_, e) =>
        {
            var resource = new HyprlandInputCaptureV1Resource(client, manager.Version, e.Session);
            EisBridge eis;
            try
            {
                eis = new EisBridge(Loop, Layout, Seat.Keyboard);
            }
            catch (Exception ex)
            {
                Log.Error($"session {e.Handle}: eis context failed: {ex.Message}");
                return;
            }

            var session = new InputCaptureSession(this, resource, e.Handle, eis);
            _sessions.Add(session);
            int fd;
            try
            {
                fd = eis.AddClientFd();
            }
            catch (Exception ex)
            {
                Log.Error($"session {e.Handle}: eis client fd failed: {ex.Message}");
                return;
            }

            resource.SendEisFd(fd);
            _ = close(fd);
            Log.Info($"session {e.Handle} created");
        };
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
