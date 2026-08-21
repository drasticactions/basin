using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Basin.Seat;

[SupportedOSPlatform("macos")]
public sealed class MacKeyboardLayout : IHostKeyboardLayout, IDisposable
{
    private readonly Timer _poll;
    private readonly object _gate = new();
    private nint _source;
    private string _name = "macos";
    private string? _keymap;
    private string? _keymapName;
    private bool _disposed;

    private MacKeyboardLayout(nint source)
    {
        _source = source;
        _poll = new Timer(_ => RequestRefresh(), null, 0, PollMillis);
    }

    public const int PollMillis = 500;

    public string Name
    {
        get
        {
            lock (_gate)
            {
                return _name;
            }
        }
    }

    public event Action? Changed;

    public static MacKeyboardLayout? TryCreate()
    {
        var source = TISCopyCurrentKeyboardLayoutInputSource();
        if (source == 0)
        {
            return null;
        }

        if (!CarriesLayoutData(source))
        {
            CFRelease(source);
            return null;
        }

        return new MacKeyboardLayout(source);
    }

    private static bool CarriesLayoutData(nint source)
    {
        var data = TISGetInputSourceProperty(source, KTISPropertyUnicodeKeyLayoutData);
        return data != 0 && CFDataGetBytePtr(data) != 0;
    }

    public bool TryReadKeymapText(out string xkb)
    {
        lock (_gate)
        {
            if (_keymap is not null)
            {
                xkb = _keymap;
                return true;
            }
        }

        if (CanReadHere())
        {
            Refresh();
        }

        lock (_gate)
        {
            xkb = _keymap ?? string.Empty;
            return _keymap is not null;
        }
    }

    private static string? BuildKeymap(nint source, string name)
    {
        var data = TISGetInputSourceProperty(source, KTISPropertyUnicodeKeyLayoutData);
        if (data == 0)
        {
            return null;
        }

        var layout = CFDataGetBytePtr(data);
        if (layout == 0)
        {
            return null;
        }

        var levels = new List<HostKeymapWriter.Levels>();
        foreach (var (code, _) in HostKeyMap.Entries)
        {
            if (!HostKeymapWriter.TryKeycodeName(code, out _) || !TryVirtualKey(code, out var virtualKey))
            {
                continue;
            }

            var plain = Translate(layout, virtualKey, 0);
            if (plain is null)
            {
                continue;
            }

            levels.Add(new HostKeymapWriter.Levels(
                code,
                plain,
                Translate(layout, virtualKey, ShiftKeyBit),
                Translate(layout, virtualKey, OptionKeyBit),
                Translate(layout, virtualKey, OptionKeyBit | ShiftKeyBit)));
        }

        if (levels.Count == 0)
        {
            return null;
        }

        return HostKeymapWriter.Write(name, levels);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _poll.Dispose();

        lock (_gate)
        {
            if (_source != 0)
            {
                CFRelease(_source);
                _source = 0;
            }
        }
    }

    private static bool CanReadHere() =>
        pthread_main_np() != 0 || MainQueue == 0 || objc_getClass("NSApplication") == 0;

    private unsafe void RequestRefresh()
    {
        if (CanReadHere())
        {
            Refresh();
            return;
        }

        var handle = GCHandle.Alloc(this);
        dispatch_async_f(MainQueue, GCHandle.ToIntPtr(handle), &OnMainQueue);
    }

    [UnmanagedCallersOnly]
    private static void OnMainQueue(nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        try
        {
            if (handle.Target is MacKeyboardLayout layout)
            {
                layout.Refresh();
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            handle.Free();
        }
    }

    private void Refresh()
    {
        var current = TISCopyCurrentKeyboardLayoutInputSource();
        if (current == 0)
        {
            return;
        }

        if (!CarriesLayoutData(current))
        {
            CFRelease(current);
            return;
        }

        var name = Describe(current);

        lock (_gate)
        {
            if (_disposed || name == _keymapName)
            {
                CFRelease(current);
                return;
            }
        }

        var keymap = BuildKeymap(current, name);

        bool changed;
        lock (_gate)
        {
            if (_disposed)
            {
                CFRelease(current);
                return;
            }

            if (_source != 0)
            {
                CFRelease(_source);
            }

            _source = current;
            changed = name != _name;
            _name = name;
            _keymap = keymap;
            _keymapName = name;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static unsafe string? Translate(nint layout, ushort virtualKey, uint modifiers)
    {
        uint deadState = 0;
        var chars = stackalloc char[8];
        nuint length = 0;
        var status = UCKeyTranslate(
            layout, virtualKey, KUCKeyActionDown, (modifiers >> 8) & 0xff, KeyboardType,
            0, ref deadState, 8, ref length, chars);

        if (status != 0)
        {
            return null;
        }

        if (length == 0 && deadState != 0)
        {
            var space = stackalloc char[8];
            nuint spaceLength = 0;
            status = UCKeyTranslate(
                layout, SpaceVirtualKey, KUCKeyActionDown, 0, KeyboardType,
                0, ref deadState, 8, ref spaceLength, space);
            return status == 0 && spaceLength == 1 ? HostKeymapWriter.DeadKeysymName(space[0]) : null;
        }

        return length == 1 ? HostKeymapWriter.KeysymName(chars[0]) : null;
    }

    private static string Describe(nint source)
    {
        var id = TISGetInputSourceProperty(source, KTISPropertyInputSourceID);
        return id == 0 ? "macos" : CopyString(id) ?? "macos";
    }

    private static string? CopyString(nint value)
    {
        var length = CFStringGetLength(value);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new byte[(length * 4) + 1];
        return CFStringGetCString(value, buffer, buffer.Length, KCFStringEncodingUtf8)
            ? System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0')
            : null;
    }

    private static bool TryVirtualKey(HostKeyCode code, out ushort virtualKey)
    {
        virtualKey = code switch
        {
            HostKeyCode.KeyA => 0,
            HostKeyCode.KeyS => 1,
            HostKeyCode.KeyD => 2,
            HostKeyCode.KeyF => 3,
            HostKeyCode.KeyH => 4,
            HostKeyCode.KeyG => 5,
            HostKeyCode.KeyZ => 6,
            HostKeyCode.KeyX => 7,
            HostKeyCode.KeyC => 8,
            HostKeyCode.KeyV => 9,
            HostKeyCode.IntlBackslash => 10,
            HostKeyCode.KeyB => 11,
            HostKeyCode.KeyQ => 12,
            HostKeyCode.KeyW => 13,
            HostKeyCode.KeyE => 14,
            HostKeyCode.KeyR => 15,
            HostKeyCode.KeyY => 16,
            HostKeyCode.KeyT => 17,
            HostKeyCode.Digit1 => 18,
            HostKeyCode.Digit2 => 19,
            HostKeyCode.Digit3 => 20,
            HostKeyCode.Digit4 => 21,
            HostKeyCode.Digit6 => 22,
            HostKeyCode.Digit5 => 23,
            HostKeyCode.Equal => 24,
            HostKeyCode.Digit9 => 25,
            HostKeyCode.Digit7 => 26,
            HostKeyCode.Minus => 27,
            HostKeyCode.Digit8 => 28,
            HostKeyCode.Digit0 => 29,
            HostKeyCode.BracketRight => 30,
            HostKeyCode.KeyO => 31,
            HostKeyCode.KeyU => 32,
            HostKeyCode.BracketLeft => 33,
            HostKeyCode.KeyI => 34,
            HostKeyCode.KeyP => 35,
            HostKeyCode.KeyL => 37,
            HostKeyCode.KeyJ => 38,
            HostKeyCode.Quote => 39,
            HostKeyCode.KeyK => 40,
            HostKeyCode.Semicolon => 41,
            HostKeyCode.Backslash => 42,
            HostKeyCode.Comma => 43,
            HostKeyCode.Slash => 44,
            HostKeyCode.KeyN => 45,
            HostKeyCode.KeyM => 46,
            HostKeyCode.Period => 47,
            HostKeyCode.Backquote => 50,
            HostKeyCode.Space => 49,
            HostKeyCode.IntlYen => 0x5D,
            HostKeyCode.IntlRo => 0x5E,
            _ => ushort.MaxValue,
        };

        return virtualKey != ushort.MaxValue;
    }

    private const uint KUCKeyActionDown = 0;
    private static readonly uint KeyboardType = LMGetKbdType();
    private const uint ShiftKeyBit = 1 << 9;
    private const uint OptionKeyBit = 1 << 11;
    private const ushort SpaceVirtualKey = 49;
    private const uint KCFStringEncodingUtf8 = 0x08000100;

    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const string LibObjc = "/usr/lib/libobjc.dylib";

    private static readonly nint MainQueue = ReadMainQueue();

    private static nint ReadMainQueue() =>
        NativeLibrary.TryLoad(LibSystem, out var handle) &&
        NativeLibrary.TryGetExport(handle, "_dispatch_main_q", out var address)
            ? address
            : 0;

    private static readonly nint KTISPropertyUnicodeKeyLayoutData =
        ReadSymbol(Carbon, "kTISPropertyUnicodeKeyLayoutData");

    private static readonly nint KTISPropertyInputSourceID =
        ReadSymbol(Carbon, "kTISPropertyInputSourceID");

    private static nint ReadSymbol(string library, string name)
    {
        if (!NativeLibrary.TryLoad(library, out var handle) ||
            !NativeLibrary.TryGetExport(handle, name, out var address))
        {
            return 0;
        }

        return Marshal.ReadIntPtr(address);
    }

    [DllImport(Carbon)]
    private static extern byte LMGetKbdType();

    [DllImport(LibSystem)]
    private static extern int pthread_main_np();

    [DllImport(LibObjc)]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(LibSystem)]
    private static extern unsafe void dispatch_async_f(
        nint queue, nint context, delegate* unmanaged<nint, void> work);

    [DllImport(Carbon)]
    private static extern nint TISCopyCurrentKeyboardLayoutInputSource();

    [DllImport(Carbon)]
    private static extern nint TISGetInputSourceProperty(nint source, nint key);

    [DllImport(Carbon)]
    private static extern unsafe int UCKeyTranslate(
        nint layout,
        ushort virtualKey,
        uint action,
        uint modifiers,
        uint keyboardType,
        uint options,
        ref uint deadState,
        nuint capacity,
        ref nuint length,
        char* output);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetBytePtr(nint data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(nint value);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(nint value);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(nint value, byte[] buffer, nint size, uint encoding);
}
