using Wayland;

namespace Basin.Portal.Client;

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

    public ClientSeat(WlSeat seat, WlSeat.Capability capabilities, ClientKeymap keymap)
    {
        _keymap = keymap;
        _seat = seat;
        OnCapabilities(_seat, capabilities);
    }

    public void SetCapabilities(WlSeat.Capability capabilities) => OnCapabilities(_seat, capabilities);

    public event Action<uint, double, double>? PointerEntered;

    public event Action<uint>? PointerLeft;

    public event Action<uint, double, double>? PointerMoved;

    public event Action<uint, uint, bool>? PointerButton;

    public event Action<uint, double, double>? PointerAxis;

    public event Action<uint, uint, bool>? Key;

    public event Action<uint, uint, uint, uint, uint>? Modifiers;

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
