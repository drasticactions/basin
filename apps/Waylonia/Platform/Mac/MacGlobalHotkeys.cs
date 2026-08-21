using System.Runtime.InteropServices;
using Basin.Avalonia;
using Basin.Diagnostics;

namespace Waylonia;

internal sealed unsafe class MacGlobalHotkeys : IDisposable
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    private const uint EventClassKeyboard = 0x6B657962;

    private const uint EventHotKeyPressed = 5;

    private const uint ParamDirectObject = 0x2D2D2D2D;

    private const uint TypeEventHotKeyId = 0x686B6964;

    private const uint SignatureTag = 0x77796C6B;

    private const uint CommandModifier = 0x0100;

    private const uint ShiftModifier = 0x0200;

    private const uint OptionModifier = 0x0800;

    private const uint ControlModifier = 0x1000;

    private static MacGlobalHotkeys? _active;

    private readonly Dictionary<uint, Hotkey> _bindings = [];

    private readonly List<IntPtr> _registrations = [];

    private readonly Action<Hotkey> _launch;

    private IntPtr _handler;

    private MacGlobalHotkeys(Action<Hotkey> launch) => _launch = launch;

    public static MacGlobalHotkeys? TryStart(IReadOnlyList<Hotkey> hotkeys, Action<Hotkey> launch)
    {
        var instance = new MacGlobalHotkeys(launch);
        var spec = new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed };
        var status = InstallEventHandler(
            GetApplicationEventTarget(), &OnHotkeyEvent, 1, &spec, IntPtr.Zero, out instance._handler);
        if (status != 0)
        {
            BasinLog.Warn($"the hotkey event handler was refused ({status}), global hotkeys are off");
            return null;
        }

        _active = instance;
        var next = 1u;
        foreach (var hotkey in hotkeys)
        {
            if (KeyCode(hotkey.Key) is not { } code)
            {
                BasinLog.Warn($"hotkey '{hotkey.Chord}': no macOS key code for '{hotkey.Key}', skipping");
                continue;
            }

            var id = new EventHotKeyId { Signature = SignatureTag, Id = next };
            status = RegisterEventHotKey(
                code, CarbonModifiers(hotkey.Modifiers), id, GetApplicationEventTarget(), 0, out var registration);
            if (status != 0)
            {
                BasinLog.Warn($"hotkey '{hotkey.Chord}' was refused ({status}), skipping");
                continue;
            }

            instance._bindings[next] = hotkey;
            instance._registrations.Add(registration);
            next++;
        }

        if (instance._bindings.Count == 0)
        {
            instance.Dispose();
            return null;
        }

        BasinLog.Debug($"{instance._bindings.Count} global hotkey(s) registered");
        return instance;
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            _ = UnregisterEventHotKey(registration);
        }

        _registrations.Clear();
        _bindings.Clear();
        if (_handler != IntPtr.Zero)
        {
            _ = RemoveEventHandler(_handler);
            _handler = IntPtr.Zero;
        }

        if (_active == this)
        {
            _active = null;
        }
    }

    [UnmanagedCallersOnly]
    private static int OnHotkeyEvent(IntPtr call, IntPtr carbonEvent, IntPtr context)
    {
        try
        {
            if (GetEventParameter(
                    carbonEvent, ParamDirectObject, TypeEventHotKeyId, IntPtr.Zero,
                    (nuint)sizeof(EventHotKeyId), IntPtr.Zero, out var id) == 0 &&
                id.Signature == SignatureTag &&
                _active is { } active &&
                active._bindings.TryGetValue(id.Id, out var hotkey))
            {
                active._launch(hotkey);
            }
        }
        catch (Exception error)
        {
            BasinLog.Warn($"hotkey dispatch failed: {error.Message}");
        }

        return 0;
    }

    private static uint CarbonModifiers(HotkeyModifiers modifiers)
    {
        var value = 0u;
        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            value |= ShiftModifier;
        }

        if ((modifiers & HotkeyModifiers.Ctrl) != 0)
        {
            value |= ControlModifier;
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            value |= OptionModifier;
        }

        if ((modifiers & HotkeyModifiers.Super) != 0)
        {
            value |= CommandModifier;
        }

        return value;
    }

    private static uint? KeyCode(string key) => key switch
    {
        "a" => 0, "b" => 11, "c" => 8, "d" => 2, "e" => 14, "f" => 3, "g" => 5,
        "h" => 4, "i" => 34, "j" => 38, "k" => 40, "l" => 37, "m" => 46, "n" => 45,
        "o" => 31, "p" => 35, "q" => 12, "r" => 15, "s" => 1, "t" => 17, "u" => 32,
        "v" => 9, "w" => 13, "x" => 7, "y" => 16, "z" => 6,
        "0" => 29, "1" => 18, "2" => 19, "3" => 20, "4" => 21,
        "5" => 23, "6" => 22, "7" => 26, "8" => 28, "9" => 25,
        "f1" => 122, "f2" => 120, "f3" => 99, "f4" => 118, "f5" => 96, "f6" => 97,
        "f7" => 98, "f8" => 100, "f9" => 101, "f10" => 109, "f11" => 103, "f12" => 111,
        "space" => 49, "enter" or "return" => 36, "tab" => 48, "escape" => 53,
        "backspace" => 51, "delete" => 117,
        "home" => 115, "end" => 119, "pageup" => 116, "pagedown" => 121,
        "left" => 123, "right" => 124, "down" => 125, "up" => 126,
        "minus" => 27, "equal" => 24, "comma" => 43, "period" => 47,
        "slash" => 44, "backslash" => 42, "semicolon" => 41, "apostrophe" => 39,
        "grave" => 50, "bracketleft" => 33, "bracketright" => 30,
        _ => null,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;

        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;

        public uint Id;
    }

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(
        IntPtr target,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> handler,
        nuint count,
        EventTypeSpec* types,
        IntPtr context,
        out IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(
        uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr registration);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr registration);

    [DllImport(Carbon)]
    private static extern int GetEventParameter(
        IntPtr carbonEvent, uint name, uint type, IntPtr actualType, nuint size, IntPtr actualSize,
        out EventHotKeyId value);
}
