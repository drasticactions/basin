using Basin.XWayland.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.XWayland;

public sealed class XWaylandKeyboardGrabManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Seat.Seat? _seat;
    private Func<WlClient, bool>? _isXwayland;
    private Grab? _active;

    public XWaylandKeyboardGrabManager(WlServerDisplay display, CompositorGlobal compositor, Seat.Seat? seat)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _seat = seat;
        _global = display.CreateGlobal(ZwpXwaylandKeyboardGrabManagerV1.Interface, Version, OnBind);
    }

    public Surface? GrabbedSurface => _active?.Surface;

    public event Action<Surface?>? GrabChanged;

    public void RestrictTo(Func<WlClient, bool> isXwayland) => _isXwayland = isXwayland;

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
