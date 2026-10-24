using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_pointer_warp_v1</c> global. A warp applies only to the surface with pointer focus, with a valid enter serial, no pointer grab, and a position inside the surface.
/// </summary>
public sealed class PointerWarpManager : IDisposable, IWpPointerWarpV1Handler
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Seat.Seat _seat;

    /// <summary>
    /// Initializes a new instance of the <see cref="PointerWarpManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="compositor">The compositor that resolves surfaces.</param>
    /// <param name="seat">The seat whose pointer the warps move.</param>
    public PointerWarpManager(WlServerDisplay display, CompositorGlobal compositor, Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(seat);
        _compositor = compositor;
        _seat = seat;
        _global = display.CreateGlobal(WpPointerWarpV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs after the pointer moves for a warp. The arguments are the surface and the position in surface-local logical coordinates.
    /// </summary>
    public event Action<Surface, double, double>? Warped;

    /// <summary>
    /// Removes the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    void IWpPointerWarpV1Handler.Destroy(WpPointerWarpV1Resource resource)
    {
    }

    void IWpPointerWarpV1Handler.WarpPointer(WpPointerWarpV1Resource resource, in WpPointerWarpV1Resource.WarpPointerEventArgs args) =>
        OnWarp(resource.Client, in args);

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new WpPointerWarpV1Resource(client, version, id, this);

    private void OnWarp(WlClient client, in WpPointerWarpV1Resource.WarpPointerEventArgs e)
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
