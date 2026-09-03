using Basin.Hypr.Protocol;
using Basin.Seat;
using Wayland;

namespace Basin.Hypr;

internal sealed class FocusGrab : IPointerGrab, IKeyboardGrab, ITouchGrab
{
    private enum SurfaceState
    {
        PendingAddition,
        Committed,
        PendingRemoval,
    }

    private readonly HyprlandFocusGrabManager _owner;
    private readonly CompositorGlobal _compositor;
    private readonly Basin.Seat.Seat _seat;
    private readonly HyprlandFocusGrabV1Resource _resource;
    private readonly Dictionary<Surface, SurfaceState> _surfaces = [];
    private readonly Dictionary<Surface, Action> _destroyHooks = [];
    private Surface? _previousKeyboardFocus;
    private bool _active;

    public FocusGrab(
        HyprlandFocusGrabManager owner,
        CompositorGlobal compositor,
        Basin.Seat.Seat seat,
        HyprlandFocusGrabV1Resource resource)
    {
        _owner = owner;
        _compositor = compositor;
        _seat = seat;
        _resource = resource;

        resource.AddSurface += (_, e) => Add(_compositor.ResolveSurface(e.Surface));
        resource.RemoveSurface += (_, e) => Remove(_compositor.ResolveSurface(e.Surface));
        resource.Commit += (_, _) => Commit(removeOnly: false);
        resource.Destroyed += (_, _) =>
        {
            Finish(sendCleared: false);
            _owner.Forget(this);
        };
    }

    public bool IsActive => _active;

    public void Enter(Surface? surface, double x, double y) => _seat.Pointer.SendEnter(surface, x, y);

    public void Motion(uint timeMs, double x, double y) => _seat.Pointer.SendMotion(timeMs, x, y);

    public uint Button(uint timeMs, uint button, WlPointer.ButtonState state)
    {
        if (state == WlPointer.ButtonState.Pressed && !IsWhitelisted(_seat.Pointer.Focus))
        {
            Finish(sendCleared: true);
            return 0;
        }

        return _seat.Pointer.SendButton(timeMs, button, state);
    }

    public void Axis(uint timeMs, in PointerAxis axis) => _seat.Pointer.SendAxis(timeMs, axis);

    void IPointerGrab.Cancel() => Finish(sendCleared: true);

    public uint Down(Surface surface, uint timeMs, int id, double x, double y)
    {
        if (!IsWhitelisted(surface))
        {
            Finish(sendCleared: true);
            return 0;
        }

        return _seat.Touch.SendDown(surface, timeMs, id, x, y);
    }

    public void Up(uint timeMs, int id) => _seat.Touch.SendUp(timeMs, id);

    public void Motion(uint timeMs, int id, double x, double y) => _seat.Touch.SendMotion(timeMs, id, x, y);

    public void Frame() => _seat.Touch.SendFrame();

    void ITouchGrab.Cancel() => _seat.Touch.SendCancel();

    public void Enter(Surface? surface, ReadOnlySpan<uint> pressedKeys)
    {
        if (IsWhitelisted(surface))
        {
            _seat.Keyboard.SendEnter(surface, pressedKeys);
            return;
        }

        if (FirstCommitted() is { } fallback)
        {
            _seat.Keyboard.SendEnter(fallback, pressedKeys);
        }
    }

    public void Key(uint timeMs, uint key, WlKeyboard.KeyState state) => _seat.Keyboard.SendKey(timeMs, key, state);

    public void Modifiers() => _seat.Keyboard.SendModifiers();

    void IKeyboardGrab.Cancel() => Finish(sendCleared: true);

    private void Add(Surface? surface)
    {
        if (surface is null || surface.IsDestroyed || _surfaces.ContainsKey(surface))
        {
            return;
        }

        _surfaces[surface] = SurfaceState.PendingAddition;
        Action hook = () =>
        {
            Remove(surface);
            Commit(removeOnly: true);
        };
        _destroyHooks[surface] = hook;
        surface.Destroyed += hook;
    }

    private void Remove(Surface? surface)
    {
        if (surface is null || !_surfaces.TryGetValue(surface, out var state))
        {
            return;
        }

        if (state == SurfaceState.PendingAddition)
        {
            Forget(surface);
        }
        else
        {
            _surfaces[surface] = SurfaceState.PendingRemoval;
        }
    }

    private void Forget(Surface surface)
    {
        _surfaces.Remove(surface);
        if (_destroyHooks.Remove(surface, out var hook))
        {
            surface.Destroyed -= hook;
        }
    }

    private void Commit(bool removeOnly)
    {
        var changed = false;
        var anyCommitted = false;
        Surface? removed = null;
        foreach (var (surface, state) in _surfaces)
        {
            switch (state)
            {
                case SurfaceState.PendingRemoval:
                    removed = surface;
                    changed = true;
                    break;

                case SurfaceState.PendingAddition when !removeOnly:
                    anyCommitted = true;
                    changed = true;
                    break;

                case SurfaceState.Committed:
                    anyCommitted = true;
                    break;
            }
        }

        while (removed is not null)
        {
            Forget(removed);
            removed = null;
            foreach (var (surface, state) in _surfaces)
            {
                if (state == SurfaceState.PendingRemoval)
                {
                    removed = surface;
                    break;
                }
            }
        }

        if (!removeOnly)
        {
            foreach (var surface in _surfaces.Keys.ToArray())
            {
                if (_surfaces[surface] == SurfaceState.PendingAddition)
                {
                    _surfaces[surface] = SurfaceState.Committed;
                }
            }
        }

        if (!changed)
        {
            return;
        }

        if (anyCommitted)
        {
            Start();
        }
        else
        {
            Finish(sendCleared: true);
        }
    }

    private void Start()
    {
        if (!_active)
        {
            _active = true;
            _owner.Activate(this);
            _previousKeyboardFocus = _seat.Keyboard.Focus;
            _seat.Pointer.StartGrab(this);
            _seat.Keyboard.StartGrab(this);
            _seat.Touch.StartGrab(this);
        }

        RefocusKeyboard();
    }

    private void RefocusKeyboard()
    {
        if (IsWhitelisted(_seat.Keyboard.Focus))
        {
            return;
        }

        if (FirstCommitted() is { } surface)
        {
            _seat.Keyboard.SendEnter(surface, _seat.Keyboard.PressedKeys.ToArray());
        }
    }

    internal void Finish(bool sendCleared)
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _owner.Deactivate(this);
        _seat.Pointer.EndGrab(this);
        _seat.Keyboard.EndGrab(this);
        _seat.Touch.EndGrab(this);
        foreach (var surface in _surfaces.Keys.ToArray())
        {
            Forget(surface);
        }

        var previous = _previousKeyboardFocus is { IsDestroyed: false } focus ? focus : null;
        _previousKeyboardFocus = null;
        if (!ReferenceEquals(_seat.Keyboard.Focus, previous))
        {
            _seat.Keyboard.SendEnter(previous, _seat.Keyboard.PressedKeys.ToArray());
        }

        if (sendCleared && !_resource.IsDestroyed)
        {
            _resource.SendCleared();
        }
    }

    private bool IsWhitelisted(Surface? surface) =>
        surface is not null && _surfaces.TryGetValue(surface, out var state) && state == SurfaceState.Committed;

    private Surface? FirstCommitted()
    {
        foreach (var (surface, state) in _surfaces)
        {
            if (state == SurfaceState.Committed && !surface.IsDestroyed)
            {
                return surface;
            }
        }

        return null;
    }
}
