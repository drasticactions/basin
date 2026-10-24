using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

/// <summary>
/// Advertises <c>hyprland_focus_grab_manager_v1</c>. A grab keeps keyboard focus on a set of surfaces and ends when the user clicks or touches outside them.
/// </summary>
public sealed class HyprlandFocusGrabManager : IDisposable, IHyprlandFocusGrabManagerV1Handler
{
    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Basin.Seat.Seat _seat;
    private readonly List<FocusGrab> _grabs = [];
    private FocusGrab? _active;

    /// <summary>
    /// Initializes a new instance of the <see cref="HyprlandFocusGrabManager"/> class.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="compositor">The compositor global that resolves <c>wl_surface</c> resources.</param>
    /// <param name="seat">The seat whose pointer, keyboard and touch a grab takes over.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public HyprlandFocusGrabManager(WlServerDisplay display, CompositorGlobal compositor, Basin.Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(seat);
        _compositor = compositor;
        _seat = seat;
        _global = display.CreateGlobal(HyprlandFocusGrabManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets a value indicating whether a grab is active now. At most one grab is active. A new grab ends the previous one.
    /// </summary>
    public bool IsGrabbing => _active is not null;

    /// <summary>
    /// Gets the number of live grab objects, active or not.
    /// </summary>
    public int GrabCount => _grabs.Count;

    /// <summary>
    /// Ends the active grab and sends <c>cleared</c> to its client. Keyboard focus goes back to the surface that had it before the grab. It does nothing when no grab is active.
    /// </summary>
    public void ClearActiveGrab() => _active?.Finish(sendCleared: true);

    /// <summary>
    /// Ends every grab without a <c>cleared</c> event and destroys the global.
    /// </summary>
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
        _ = new HyprlandFocusGrabManagerV1Resource(client, version, id, this);
    }

    void IHyprlandFocusGrabManagerV1Handler.CreateGrab(HyprlandFocusGrabManagerV1Resource resource, in HyprlandFocusGrabManagerV1Resource.CreateGrabEventArgs args) =>
        _grabs.Add(new FocusGrab(this, _compositor, _seat, resource.Client, resource.Version, args.Grab));

    void IHyprlandFocusGrabManagerV1Handler.Destroy(HyprlandFocusGrabManagerV1Resource resource)
    {
    }
}
