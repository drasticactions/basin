using Basin.Scene;
using Basin.Seat;

namespace Basin.Inspector;

/// <summary>
/// The input path of an inspector overlay. A consumer gives each pointer event, key and touch to it before its own handling, and skips the events that it takes.
/// </summary>
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

    /// <summary>
    /// Gets the index of the overlay surface. It is separate from the consumer's chrome index.
    /// </summary>
    public UISurfaceIndex Index { get; }

    /// <summary>
    /// Gets the router that sends the taken events to the overlay surface. It uses an implicit grab.
    /// </summary>
    public UISurfaceRouter Router { get; }

    /// <summary>
    /// Gets a value indicating whether the overlay is open and has keyboard focus.
    /// </summary>
    public bool HasKeyboard => _active() && Router.KeyboardFocus is not null;

    /// <summary>
    /// Gets the number of pointer motions that the overlay took.
    /// </summary>
    public int Motions { get; private set; }

    /// <summary>
    /// Gets the number of button presses that the overlay took.
    /// </summary>
    public int Presses { get; private set; }

    /// <summary>
    /// Gets the number of axis events that the overlay took.
    /// </summary>
    public int Scrolls { get; private set; }

    /// <summary>
    /// Gets the number of key events that the overlay took.
    /// </summary>
    public int Keys { get; private set; }

    /// <summary>
    /// Gets the number of touch contacts that the overlay took.
    /// </summary>
    public int Touches { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the overlay holds a button press or a touch contact. While it does, the overlay takes each motion, also outside its windows.
    /// </summary>
    public bool HoldsButton => _held.Count > 0 || _touches.Count > 0;

    /// <summary>
    /// Occurs when the overlay gives up keyboard focus: on a press outside its windows, or when it closes. A consumer gives the keyboard back to its focused window there.
    /// </summary>
    public event Action? KeyboardReleased;

    /// <summary>
    /// Offers a pointer motion to the overlay. When the overlay takes it, the seat pointer loses its focus.
    /// </summary>
    /// <param name="time">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <param name="cursor">The cursor name to show when the overlay takes the motion, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the overlay took the motion.</returns>
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

    /// <summary>
    /// Offers a button event to the overlay. A press on the overlay takes keyboard focus from the seat. A release goes to the overlay only when it took the press of that button.
    /// </summary>
    /// <param name="time">The event time in milliseconds.</param>
    /// <param name="button">The Linux button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when the overlay took the event.</returns>
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

    /// <summary>
    /// Offers an axis event to the overlay. It takes the event only over its windows or during a grab.
    /// </summary>
    /// <param name="time">The event time in milliseconds.</param>
    /// <param name="axis">The scroll values.</param>
    /// <returns><see langword="true"/> when the overlay took the event.</returns>
    public bool Axis(uint time, in PointerAxis axis)
    {
        if (!_active() || (Router.PointerGrab ?? Router.Hovered) is null || !Router.PointerAxis(time, axis))
        {
            return false;
        }

        Scrolls++;
        return true;
    }

    /// <summary>
    /// Offers a key to the overlay. When the overlay has keyboard focus, it takes the key and marks it as consumed on the seat keyboard.
    /// </summary>
    /// <param name="time">The event time in milliseconds.</param>
    /// <param name="key">The Linux key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when the overlay took the key.</returns>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void Cancel()
    {
        if (_touches.Count > 0)
        {
            _touches.Clear();
            Router.TouchCancel();
        }
    }

    /// <summary>
    /// Drops the held buttons and contacts, the keyboard focus and the hover. <see cref="KeyboardReleased"/> occurs when the overlay had keyboard focus.
    /// </summary>
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
