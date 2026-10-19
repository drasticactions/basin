using Basin.Scene;
using Basin.Seat;

namespace Basin.Inspector;

public sealed class InspectorInput : ITouchChrome
{
    private readonly SeatKeyboard _keyboard;
    private readonly SeatPointer _pointer;
    private readonly Func<bool> _active;
    private readonly HashSet<uint> _held = [];
    private readonly HashSet<int> _touches = [];

    internal InspectorInput(Scene.Scene scene, SeatKeyboard keyboard, SeatPointer pointer, Func<bool> active)
    {
        _keyboard = keyboard;
        _pointer = pointer;
        _active = active;
        Index = new UISurfaceIndex();
        Router = new UISurfaceRouter(scene, Index) { ImplicitGrab = true };
    }

    public UISurfaceIndex Index { get; }

    public UISurfaceRouter Router { get; }

    public bool HasKeyboard => _active() && Router.KeyboardFocus is not null;

    public int Motions { get; private set; }

    public int Presses { get; private set; }

    public int Scrolls { get; private set; }

    public int Keys { get; private set; }

    public int Touches { get; private set; }

    public bool HoldsButton => _held.Count > 0 || _touches.Count > 0;

    public event Action? KeyboardReleased;

    public bool Motion(uint time, double x, double y, out string? cursor)
    {
        cursor = null;
        if (!_active())
        {
            return false;
        }

        var route = Router.PointerMotion(time, x, y);
        if (route.Surface is null && Router.PointerGrab is null && _held.Count == 0)
        {
            return false;
        }

        _pointer.NotifyClearFocus();
        Motions++;
        cursor = route.Cursor ?? "default";
        return true;
    }

    public bool Button(uint time, uint button, bool pressed)
    {
        if (!pressed)
        {
            if (!_held.Remove(button))
            {
                return false;
            }

            Router.PointerButton(time, button, pressed);
            return true;
        }

        if (!_active())
        {
            return false;
        }

        if ((Router.PointerGrab ?? Router.Hovered) is not { } surface)
        {
            if (Router.KeyboardFocus is not null)
            {
                Router.SetKeyboardFocus(null);
                KeyboardReleased?.Invoke();
            }

            return false;
        }

        if (!ReferenceEquals(Router.KeyboardFocus, surface))
        {
            _keyboard.NotifyClearFocus();
            Router.SetKeyboardFocus(surface);
            ForwardModifiers();
        }

        _held.Add(button);
        Presses++;
        Router.PointerButton(time, button, pressed);
        return true;
    }

    public bool Axis(uint time, in PointerAxis axis)
    {
        if (!_active() || (Router.PointerGrab ?? Router.Hovered) is null || !Router.PointerAxis(time, axis))
        {
            return false;
        }

        Scrolls++;
        return true;
    }

    public bool Key(uint time, uint key, bool pressed)
    {
        if (!HasKeyboard)
        {
            return false;
        }

        Router.Key(time, key, pressed);
        Keys++;
        _keyboard.NotifyKeyConsumed(key, pressed);
        return true;
    }

    public bool TryPress(int id, uint timeMs, double x, double y)
    {
        if (!_active() || Router.SurfaceAt(x, y) is not { } hit)
        {
            return false;
        }

        if (!ReferenceEquals(Router.KeyboardFocus, hit.Surface))
        {
            _keyboard.NotifyClearFocus();
            Router.SetKeyboardFocus(hit.Surface);
            ForwardModifiers();
        }

        if (!Router.TouchDown(timeMs, id, x, y))
        {
            return false;
        }

        _touches.Add(id);
        Touches++;
        return true;
    }

    public void Motion(int id, uint timeMs, double x, double y)
    {
        if (_touches.Contains(id))
        {
            Router.TouchMotion(timeMs, id, x, y);
        }
    }

    void ITouchChrome.Release(int id, uint timeMs, double x, double y)
    {
        if (_touches.Remove(id))
        {
            Router.TouchUp(timeMs, id);
        }
    }

    public void Cancel()
    {
        if (_touches.Count > 0)
        {
            _touches.Clear();
            Router.TouchCancel();
        }
    }

    public void Release()
    {
        _held.Clear();
        Cancel();
        var had = Router.KeyboardFocus is not null;
        Router.SetKeyboardFocus(null);
        Router.PointerLeave();
        if (had)
        {
            KeyboardReleased?.Invoke();
        }
    }

    internal void ForwardModifiers()
    {
        if (Router.KeyboardFocus is not null)
        {
            var (depressed, latched, locked, group) = _keyboard.ModifierState;
            Router.Modifiers(depressed, latched, locked, group);
        }
    }
}
