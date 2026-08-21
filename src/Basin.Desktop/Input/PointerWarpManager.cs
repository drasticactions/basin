using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class PointerWarpManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Seat.Seat _seat;

    public PointerWarpManager(WlServerDisplay display, CompositorGlobal compositor, Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(seat);
        _compositor = compositor;
        _seat = seat;
        _global = display.CreateGlobal(WpPointerWarpV1.Interface, Version, OnBind);
    }

    public event Action<Surface, double, double>? Warped;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpPointerWarpV1Resource(client, version, id);
        manager.WarpPointer += (_, e) => OnWarp(client, e);
    }

    private void OnWarp(WlClient client, WpPointerWarpV1Resource.WarpPointerEventArgs e)
    {
        var pointer = _seat.Pointer;
        if (_compositor.ResolveSurface(e.Surface) is not { } surface ||
            surface != pointer.Focus ||
            e.Pointer?.Client != client ||
            !pointer.ValidateEnterSerial(e.Serial))
        {
            return;
        }

        if (pointer.HasGrab)
        {
            return;
        }

        var x = e.X.ToDouble();
        var y = e.Y.ToDouble();
        if (x < 0 || y < 0 || x > surface.Current.Width || y > surface.Current.Height)
        {
            return;
        }

        pointer.NotifyWarp((uint)(MonotonicClock.Nanos / 1_000_000), x, y);
        Warped?.Invoke(surface, x, y);
    }
}
