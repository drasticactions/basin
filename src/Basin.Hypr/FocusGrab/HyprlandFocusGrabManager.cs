using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

public sealed class HyprlandFocusGrabManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Basin.Seat.Seat _seat;
    private readonly List<FocusGrab> _grabs = [];
    private FocusGrab? _active;

    public HyprlandFocusGrabManager(WlServerDisplay display, CompositorGlobal compositor, Basin.Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(seat);
        _compositor = compositor;
        _seat = seat;
        _global = display.CreateGlobal(HyprlandFocusGrabManagerV1.Interface, Version, OnBind);
    }

    public bool IsGrabbing => _active is not null;

    public int GrabCount => _grabs.Count;

    public void ClearActiveGrab() => _active?.Finish(sendCleared: true);

    public void Dispose()
    {
        for (var i = _grabs.Count - 1; i >= 0; i--)
        {
            _grabs[i].Finish(sendCleared: false);
        }

        _global.Dispose();
    }

    internal void Activate(FocusGrab grab)
    {
        if (_active is { } previous && !ReferenceEquals(previous, grab))
        {
            previous.Finish(sendCleared: true);
        }

        _active = grab;
    }

    internal void Deactivate(FocusGrab grab)
    {
        if (ReferenceEquals(_active, grab))
        {
            _active = null;
        }
    }

    internal void Forget(FocusGrab grab) => _grabs.Remove(grab);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandFocusGrabManagerV1Resource(client, version, id);
        manager.CreateGrab += (_, e) =>
        {
            var resource = new HyprlandFocusGrabV1Resource(client, manager.Version, e.Grab);
            _grabs.Add(new FocusGrab(this, _compositor, _seat, resource));
        };
    }
}
