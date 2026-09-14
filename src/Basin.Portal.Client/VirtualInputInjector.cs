using Basin.Capabilities;
using Basin.Portal.Client.Protocol;
using Wayland;

namespace Basin.Portal.Client;

public sealed class VirtualInputInjector : IInputSink, IDisposable
{
    private readonly ZwpVirtualKeyboardManagerV1? _keyboards;
    private readonly ZwlrVirtualPointerManagerV1? _pointers;
    private readonly WlSeat? _seat;
    private readonly ClientKeymap _keymap;
    private ZwlrVirtualPointerV1? _pointer;

    public VirtualInputInjector(ZwpVirtualKeyboardManagerV1? keyboards, ZwlrVirtualPointerManagerV1? pointers, WlSeat? seat, ClientKeymap keymap)
    {
        _keyboards = keyboards;
        _pointers = pointers;
        _seat = seat;
        _keymap = keymap;
    }

    public InputDeviceCapability Supports()
    {
        var caps = InputDeviceCapability.None;
        if (_keyboards is not null)
        {
            caps |= InputDeviceCapability.Keyboard;
        }

        if (_pointers is not null)
        {
            caps |= InputDeviceCapability.Pointer;
        }

        return caps;
    }

    public IInjectedKeyboard? CreateKeyboard()
    {
        if (_keyboards is not { } manager || _seat is not { } seat)
        {
            return null;
        }

        var keyboard = manager.CreateVirtualKeyboard(seat);
        if (_keymap.KeymapBuffer is { } buffer)
        {
            keyboard.Keymap(Wayland.WlKeyboard.KeymapFormat.XkbV1, buffer.Fd, buffer.Size);
        }

        return new VirtualKeyboard(keyboard);
    }

    public bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed)
    {
        if (keyboard is not VirtualKeyboard vkbd)
        {
            return false;
        }

        vkbd.Proxy.Key(timeMs, keycode, pressed ? 1u : 0u);
        return true;
    }

    public bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group)
    {
        if (keyboard is not VirtualKeyboard vkbd)
        {
            return false;
        }

        vkbd.Proxy.Modifiers(depressed, latched, locked, group);
        return true;
    }

    public bool PointerMotion(uint timeMs, double dx, double dy)
    {
        if (Pointer() is not { } pointer)
        {
            return false;
        }

        pointer.Motion(timeMs, WlFixed.FromDouble(dx), WlFixed.FromDouble(dy));
        return true;
    }

    public bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
    {
        if (Pointer() is not { } pointer || width <= 0 || height <= 0)
        {
            return false;
        }

        pointer.MotionAbsolute(timeMs, (uint)Math.Clamp(x, 0, width), (uint)Math.Clamp(y, 0, height), (uint)width, (uint)height);
        return true;
    }

    public bool PointerButton(uint timeMs, uint button, bool pressed)
    {
        if (Pointer() is not { } pointer)
        {
            return false;
        }

        pointer.Button(timeMs, button, pressed ? WlPointer.ButtonState.Pressed : WlPointer.ButtonState.Released);
        return true;
    }

    public bool PointerAxis(uint timeMs, uint axis, double value)
    {
        if (Pointer() is not { } pointer)
        {
            return false;
        }

        pointer.Axis(timeMs, (WlPointer.Axis)axis, WlFixed.FromDouble(value));
        return true;
    }

    public bool PointerAxisSource(uint source)
    {
        if (Pointer() is not { } pointer)
        {
            return false;
        }

        pointer.AxisSource((WlPointer.AxisSource)source);
        return true;
    }

    public bool PointerAxisStop(uint timeMs, uint axis)
    {
        if (Pointer() is not { } pointer)
        {
            return false;
        }

        pointer.AxisStop(timeMs, (WlPointer.Axis)axis);
        return true;
    }

    public bool Frame()
    {
        if (Pointer() is not { } pointer)
        {
            return false;
        }

        pointer.Frame();
        return true;
    }

    public void Dispose()
    {
        if (_pointer is { IsDestroyed: false } pointer)
        {
            pointer.Destroy();
        }

        _pointer = null;
    }

    private ZwlrVirtualPointerV1? Pointer()
    {
        if (_pointer is { IsDestroyed: false })
        {
            return _pointer;
        }

        if (_pointers is not { } manager)
        {
            return null;
        }

        _pointer = manager.CreateVirtualPointer(_seat);
        return _pointer;
    }

    private sealed class VirtualKeyboard : IInjectedKeyboard
    {
        public VirtualKeyboard(ZwpVirtualKeyboardV1 proxy) => Proxy = proxy;

        public ZwpVirtualKeyboardV1 Proxy { get; }

        public object? Tag { get; set; }

        public bool SetKeymap(ReadOnlySpan<byte> keymapText)
        {
            return false;
        }

        public void Dispose()
        {
            if (!Proxy.IsDestroyed)
            {
                Proxy.Destroy();
            }
        }
    }
}
