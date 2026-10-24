using Basin.XWayland.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.XWayland;

/// <summary>
/// Advertises <c>zwp_xwayland_keyboard_grab_manager_v1</c>. A grab sends every key to one surface until the grab object or the surface is destroyed.
/// </summary>
public sealed class XWaylandKeyboardGrabManager : IDisposable
{
    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Seat.Seat? _seat;
    private Func<WlClient, bool>? _isXwayland;
    private Grab? _active;

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandKeyboardGrabManager"/> class.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="compositor">The compositor global that resolves <c>wl_surface</c> resources.</param>
    /// <param name="seat">The seat whose keyboard a grab takes. When it is <see langword="null"/>, requests create inert grab objects.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> or <paramref name="compositor"/> is <see langword="null"/>.</exception>
    public XWaylandKeyboardGrabManager(WlServerDisplay display, CompositorGlobal compositor, Seat.Seat? seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _seat = seat;
        _global = display.CreateGlobal(ZwpXwaylandKeyboardGrabManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets the surface that holds the grab, or <see langword="null"/> when no grab is active.
    /// </summary>
    public Surface? GrabbedSurface => _active?.Surface;

    /// <summary>
    /// Occurs when a grab starts or ends. The argument is the new grabbed surface, or <see langword="null"/> when the grab ended.
    /// </summary>
    public event Action<Surface?>? GrabChanged;

    /// <summary>
    /// Limits grabs to Xwayland clients. A request from another client creates an inert grab object.
    /// </summary>
    /// <param name="isXwayland">The check that identifies an Xwayland client.</param>
    public void RestrictTo(Func<WlClient, bool> isXwayland) => _isXwayland = isXwayland;

    /// <summary>
    /// Ends the active grab and destroys the global.
    /// </summary>
    public void Dispose()
    {
        Release();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpXwaylandKeyboardGrabManagerV1Resource(client, version, id);
        manager.GrabKeyboard += (_, e) =>
        {
            var resource = new ZwpXwaylandKeyboardGrabV1Resource(client, manager.Version, e.Id);

            if (_isXwayland is { } check && !check(client))
            {
                return;
            }

            if (_seat is not { } seat || _compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            Release();
            _active = new Grab(this, resource, surface, seat);
            seat.Keyboard.StartGrab(_active);
            GrabChanged?.Invoke(surface);
        };
    }

    private void Release()
    {
        if (_active is { } grab)
        {
            _active = null;
            grab.Seat.Keyboard.EndGrab(grab);
            GrabChanged?.Invoke(null);
        }
    }

    private sealed class Grab : Basin.Seat.IKeyboardGrab
    {
        private readonly XWaylandKeyboardGrabManager _owner;

        public Grab(
            XWaylandKeyboardGrabManager owner,
            ZwpXwaylandKeyboardGrabV1Resource resource,
            Surface surface,
            Seat.Seat seat)
        {
            _owner = owner;
            Surface = surface;
            Seat = seat;
            resource.Destroyed += (_, _) => End();
            surface.Destroyed += End;
        }

        public Surface Surface { get; }

        public Seat.Seat Seat { get; }

        public void Enter(Surface? surface, ReadOnlySpan<uint> pressedKeys) =>
            Seat.Keyboard.SendEnter(Surface, pressedKeys);

        public void Key(uint timeMs, uint key, WlKeyboard.KeyState state) =>
            Seat.Keyboard.SendKey(timeMs, key, state);

        public void Modifiers() => Seat.Keyboard.SendModifiers();

        public void Cancel()
        {
        }

        private void End()
        {
            if (ReferenceEquals(_owner._active, this))
            {
                _owner.Release();
            }
        }
    }
}
