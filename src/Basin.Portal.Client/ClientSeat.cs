using Wayland;

namespace Basin.Portal.Client;

/// <summary>
/// Receives pointer and keyboard events for the surfaces of a portal client, for example its prompt windows.
/// Each event carries the protocol id of the focused surface, and no event fires without a focus.
/// </summary>
public sealed class ClientSeat : IDisposable
{
    private readonly ClientKeymap _keymap;
    private WlPointer? _pointer;
    private WlKeyboard? _keyboard;
    private uint _pointerFocus;
    private uint _keyboardFocus;
    private double _x;
    private double _y;

    private readonly WlSeat _seat;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientSeat"/> class and gets a pointer and a keyboard for the capabilities present now.
    /// </summary>
    /// <param name="seat">The bound seat.</param>
    /// <param name="capabilities">The capabilities that the seat announced.</param>
    /// <param name="keymap">The keymap that receives the keymap of the seat keyboard.</param>
    public ClientSeat(WlSeat seat, WlSeat.Capability capabilities, ClientKeymap keymap)
    {
        _keymap = keymap;
        _seat = seat;
        OnCapabilities(_seat, capabilities);
    }

    /// <summary>
    /// Gets a pointer or a keyboard when a new capability appears. A capability that goes away keeps its device.
    /// </summary>
    /// <param name="capabilities">The new capabilities of the seat.</param>
    public void SetCapabilities(WlSeat.Capability capabilities) => OnCapabilities(_seat, capabilities);

    /// <summary>
    /// Occurs when the pointer enters a surface. The arguments are the surface id and the position in surface coordinates.
    /// </summary>
    public event Action<uint, double, double>? PointerEntered;

    /// <summary>
    /// Occurs when the pointer leaves a surface. The argument is the surface id.
    /// </summary>
    public event Action<uint>? PointerLeft;

    /// <summary>
    /// Occurs when the pointer moves over the focused surface. The arguments are the surface id and the position in surface coordinates.
    /// </summary>
    public event Action<uint, double, double>? PointerMoved;

    /// <summary>
    /// Occurs on a button press or release. The arguments are the surface id, the evdev button code and <see langword="true"/> for a press.
    /// </summary>
    public event Action<uint, uint, bool>? PointerButton;

    /// <summary>
    /// Occurs on a scroll. The arguments are the surface id and the horizontal and vertical scroll values. One of the two values is always 0.
    /// </summary>
    public event Action<uint, double, double>? PointerAxis;

    /// <summary>
    /// Occurs on a key press or release. The arguments are the surface id, the evdev keycode and <see langword="true"/> for a press.
    /// </summary>
    public event Action<uint, uint, bool>? Key;

    /// <summary>
    /// Occurs when the modifier state changes. The arguments are the surface id, the depressed, latched and locked masks, and the group.
    /// </summary>
    public event Action<uint, uint, uint, uint, uint>? Modifiers;

    /// <summary>
    /// Destroys the pointer and the keyboard objects. It does not destroy the seat.
    /// </summary>
    public void Dispose()
    {
        if (_pointer is { IsDestroyed: false } pointer)
        {
            pointer.Dispose();
        }

        if (_keyboard is { IsDestroyed: false } keyboard)
        {
            keyboard.Dispose();
        }
    }

    private void OnCapabilities(WlSeat seat, WlSeat.Capability capabilities)
    {
        if (capabilities.HasFlag(WlSeat.Capability.Pointer) && _pointer is null)
        {
            var pointer = seat.GetPointer();
            _pointer = pointer;
            pointer.Enter += (_, e) =>
            {
                _pointerFocus = e.Surface?.Id ?? 0;
                _x = e.SurfaceX.ToDouble();
                _y = e.SurfaceY.ToDouble();
                if (_pointerFocus != 0)
                {
                    PointerEntered?.Invoke(_pointerFocus, _x, _y);
                }
            };
            pointer.Leave += (_, _) =>
            {
                if (_pointerFocus != 0)
                {
                    PointerLeft?.Invoke(_pointerFocus);
                }

                _pointerFocus = 0;
            };
            pointer.Motion += (_, e) =>
            {
                _x = e.SurfaceX.ToDouble();
                _y = e.SurfaceY.ToDouble();
                if (_pointerFocus != 0)
                {
                    PointerMoved?.Invoke(_pointerFocus, _x, _y);
                }
            };
            pointer.Button += (_, e) =>
            {
                if (_pointerFocus != 0)
                {
                    PointerButton?.Invoke(_pointerFocus, e.Button, e.State == WlPointer.ButtonState.Pressed);
                }
            };
            pointer.AxisEvent += (_, e) =>
            {
                if (_pointerFocus != 0)
                {
                    var value = e.Value.ToDouble();
                    PointerAxis?.Invoke(_pointerFocus, e.Axis == WlPointer.Axis.HorizontalScroll ? value : 0, e.Axis == WlPointer.Axis.VerticalScroll ? value : 0);
                }
            };
        }

        if (capabilities.HasFlag(WlSeat.Capability.Keyboard) && _keyboard is null)
        {
            var keyboard = seat.GetKeyboard();
            _keyboard = keyboard;
            keyboard.Keymap += (_, e) =>
            {
                if (e.Format == WlKeyboard.KeymapFormat.XkbV1)
                {
                    _keymap.Load(e.Fd, e.Size);
                }

                CloseFd(e.Fd);
            };
            keyboard.Enter += (_, e) => _keyboardFocus = e.Surface?.Id ?? 0;
            keyboard.Leave += (_, _) => _keyboardFocus = 0;
            keyboard.Key += (_, e) =>
            {
                if (_keyboardFocus != 0)
                {
                    Key?.Invoke(_keyboardFocus, e.Key, e.State == WlKeyboard.KeyState.Pressed);
                }
            };
            keyboard.Modifiers += (_, e) =>
            {
                _keymap.UpdateModifiers(e.ModsDepressed, e.ModsLatched, e.ModsLocked, e.Group);
                if (_keyboardFocus != 0)
                {
                    Modifiers?.Invoke(_keyboardFocus, e.ModsDepressed, e.ModsLatched, e.ModsLocked, e.Group);
                }
            };
        }
    }

    private static void CloseFd(int fd)
    {
        if (fd >= 0)
        {
            _ = close(fd);
        }
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);
}
