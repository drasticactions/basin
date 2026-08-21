using System.Text;
using Basin.Diagnostics;
using Xcb.Native;

namespace Basin.XWayland;

public sealed unsafe class X11HotkeyGrabber : IDisposable
{
    private const byte EventKeyPress = 2;
    private const byte EventMappingNotify = 34;
    private const byte MappingKeyboard = 1;
    private const byte GrabModeAsync = 1;
    private const byte AnyKey = 0;
    private const ushort AnyModifier = 0x8000;
    private const ushort ShiftMask = 0x0001;
    private const ushort LockMask = 0x0002;
    private const ushort ControlMask = 0x0004;
    private const ushort Mod1Mask = 0x0008;
    private const ushort Mod2Mask = 0x0010;
    private const ushort Mod4Mask = 0x0040;
    private const ushort ChordMask = ShiftMask | ControlMask | Mod1Mask | Mod4Mask;

    private sealed class Binding
    {
        internal required ushort Mask;
        internal required string Key;
        internal required Action Activated;
        internal readonly List<byte> Keycodes = [];
    }

    private readonly xcb_connection_t* _conn;
    private readonly IEventSource _source;
    private readonly uint _root;
    private readonly List<Binding> _bindings = [];
    private byte _minKeycode;
    private uint[] _keysyms = [];
    private int _keysymsPerKeycode;
    private bool _dead;
    private bool _disposed;

    private X11HotkeyGrabber(ICompositorEventLoop loop, xcb_connection_t* conn, int screen)
    {
        _conn = conn;
        var screens = Libxcb.xcb_setup_roots_iterator(Libxcb.xcb_get_setup(conn));
        for (var i = 0; i < screen && screens.rem > 1; i++)
        {
            Libxcb.xcb_screen_next(&screens);
        }

        _root = screens.data->root;
        RefreshKeyboardMapping();
        var fd = Libxcb.xcb_get_file_descriptor(conn);
        _source = loop.AddFd(fd, FdReadiness.Readable, (_, _) => Pump());
        BasinCounters.Track();
    }

    public static X11HotkeyGrabber? TryConnect(ICompositorEventLoop loop, string? display = null)
    {
        xcb_connection_t* conn;
        int screen;
        if (display is null)
        {
            conn = Libxcb.xcb_connect(null, &screen);
        }
        else
        {
            var bytes = Encoding.ASCII.GetBytes(display + "\0");
            fixed (byte* ptr = bytes)
            {
                conn = Libxcb.xcb_connect((sbyte*)ptr, &screen);
            }
        }

        if (Libxcb.xcb_connection_has_error(conn) != 0)
        {
            BasinLog.Warn($"no X server answers on '{display ?? Environment.GetEnvironmentVariable("DISPLAY")}'");
            Libxcb.xcb_disconnect(conn);
            return null;
        }

        return new X11HotkeyGrabber(loop, conn, screen);
    }

    public bool TryGrab(X11HotkeyModifiers modifiers, string key, Action activated)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dead)
        {
            return false;
        }

        if (Keysym(key) is not { } keysym)
        {
            BasinLog.Warn($"no X keysym for '{key}'");
            return false;
        }

        var binding = new Binding { Mask = Mask(modifiers), Key = key, Activated = activated };
        if (!Grab(binding))
        {
            return false;
        }

        _bindings.Add(binding);
        _ = Libxcb.xcb_flush(_conn);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_dead)
        {
            _ = Libxcb.xcb_ungrab_key(_conn, AnyKey, _root, AnyModifier);
            _ = Libxcb.xcb_flush(_conn);
        }

        _source.Remove();
        Libxcb.xcb_disconnect(_conn);
        _bindings.Clear();
        BasinCounters.Untrack();
    }

    private bool Grab(Binding binding)
    {
        binding.Keycodes.Clear();
        var keysym = Keysym(binding.Key)!.Value;
        var count = _keysyms.Length / Math.Max(1, _keysymsPerKeycode);
        for (var i = 0; i < count; i++)
        {
            for (var column = 0; column < _keysymsPerKeycode; column++)
            {
                if (_keysyms[i * _keysymsPerKeycode + column] == keysym)
                {
                    binding.Keycodes.Add((byte)(_minKeycode + i));
                    break;
                }
            }
        }

        if (binding.Keycodes.Count == 0)
        {
            BasinLog.Warn($"the keyboard mapping has no keycode for '{binding.Key}'");
            return false;
        }

        Span<ushort> variants = [binding.Mask, (ushort)(binding.Mask | LockMask), (ushort)(binding.Mask | Mod2Mask), (ushort)(binding.Mask | LockMask | Mod2Mask)];
        foreach (var keycode in binding.Keycodes)
        {
            foreach (var variant in variants)
            {
                var cookie = Libxcb.xcb_grab_key_checked(_conn, 0, _root, variant, keycode, GrabModeAsync, GrabModeAsync);
                var error = Libxcb.xcb_request_check(_conn, cookie);
                if (error != null)
                {
                    var code = error->error_code;
                    Libc.Free(error);
                    BasinLog.Warn($"the X server refused the grab for '{binding.Key}' (error {code})");
                    Ungrab(binding);
                    return false;
                }
            }
        }

        return true;
    }

    private void Ungrab(Binding binding)
    {
        foreach (var keycode in binding.Keycodes)
        {
            _ = Libxcb.xcb_ungrab_key(_conn, keycode, _root, AnyModifier);
        }

        binding.Keycodes.Clear();
    }

    private void RefreshKeyboardMapping()
    {
        var setup = Libxcb.xcb_get_setup(_conn);
        _minKeycode = setup->min_keycode;
        var count = (byte)(setup->max_keycode - setup->min_keycode + 1);
        var cookie = Libxcb.xcb_get_keyboard_mapping(_conn, _minKeycode, count);
        var reply = Libxcb.xcb_get_keyboard_mapping_reply(_conn, cookie, null);
        if (reply == null)
        {
            _keysyms = [];
            _keysymsPerKeycode = 0;
            return;
        }

        _keysymsPerKeycode = reply->keysyms_per_keycode;
        var length = Libxcb.xcb_get_keyboard_mapping_keysyms_length(reply);
        var keysyms = Libxcb.xcb_get_keyboard_mapping_keysyms(reply);
        _keysyms = new uint[length];
        for (var i = 0; i < length; i++)
        {
            _keysyms[i] = keysyms[i];
        }

        Libc.Free(reply);
    }

    private void Pump()
    {
        if (_dead)
        {
            return;
        }

        while (true)
        {
            var ev = Libxcb.xcb_poll_for_event(_conn);
            if (ev == null)
            {
                break;
            }

            try
            {
                Dispatch(ev);
            }
            catch (Exception error)
            {
                BasinLog.Warn($"hotkey dispatch failed: {error.Message}");
            }

            Libc.Free(ev);
        }

        if (Libxcb.xcb_connection_has_error(_conn) != 0)
        {
            _dead = true;
            _source.Remove();
            BasinLog.Warn($"the X server connection was lost, global hotkeys are off");
            return;
        }

        _ = Libxcb.xcb_flush(_conn);
    }

    private void Dispatch(xcb_generic_event_t* ev)
    {
        switch (ev->response_type & 0x7F)
        {
            case EventKeyPress:
            {
                var e = (xcb_key_press_event_t*)ev;
                foreach (var binding in _bindings)
                {
                    if ((e->state & ChordMask) == binding.Mask && binding.Keycodes.Contains(e->detail))
                    {
                        binding.Activated();
                        break;
                    }
                }

                break;
            }

            case EventMappingNotify:
            {
                var e = (xcb_mapping_notify_event_t*)ev;
                if (e->request == MappingKeyboard)
                {
                    RefreshKeyboardMapping();
                    foreach (var binding in _bindings)
                    {
                        Ungrab(binding);
                        _ = Grab(binding);
                    }

                    _ = Libxcb.xcb_flush(_conn);
                }

                break;
            }
        }
    }

    private static ushort Mask(X11HotkeyModifiers modifiers)
    {
        ushort mask = 0;
        if ((modifiers & X11HotkeyModifiers.Shift) != 0)
        {
            mask |= ShiftMask;
        }

        if ((modifiers & X11HotkeyModifiers.Ctrl) != 0)
        {
            mask |= ControlMask;
        }

        if ((modifiers & X11HotkeyModifiers.Alt) != 0)
        {
            mask |= Mod1Mask;
        }

        if ((modifiers & X11HotkeyModifiers.Super) != 0)
        {
            mask |= Mod4Mask;
        }

        return mask;
    }

    private static uint? Keysym(string key) => key switch
    {
        { Length: 1 } when key[0] is >= 'a' and <= 'z' or >= '0' and <= '9' => key[0],
        "f1" => 0xFFBE, "f2" => 0xFFBF, "f3" => 0xFFC0, "f4" => 0xFFC1, "f5" => 0xFFC2, "f6" => 0xFFC3,
        "f7" => 0xFFC4, "f8" => 0xFFC5, "f9" => 0xFFC6, "f10" => 0xFFC7, "f11" => 0xFFC8, "f12" => 0xFFC9,
        "space" => 0x20, "enter" or "return" => 0xFF0D, "tab" => 0xFF09, "escape" => 0xFF1B,
        "backspace" => 0xFF08, "delete" => 0xFFFF, "insert" => 0xFF63,
        "home" => 0xFF50, "end" => 0xFF57, "pageup" => 0xFF55, "pagedown" => 0xFF56,
        "left" => 0xFF51, "up" => 0xFF52, "right" => 0xFF53, "down" => 0xFF54,
        "minus" => 0x2D, "equal" => 0x3D, "comma" => 0x2C, "period" => 0x2E,
        "slash" => 0x2F, "backslash" => 0x5C, "semicolon" => 0x3B, "apostrophe" => 0x27,
        "grave" => 0x60, "bracketleft" => 0x5B, "bracketright" => 0x5D,
        _ => null,
    };
}
