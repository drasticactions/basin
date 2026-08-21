using System.Runtime.InteropServices;
using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Diagnostics;

namespace Waylonia;

internal sealed class WindowsGlobalHotkeys : IDisposable
{
    private const uint HotkeyMessage = 0x0312;

    private const uint AltModifier = 0x0001;

    private const uint ControlModifier = 0x0002;

    private const uint ShiftModifier = 0x0004;

    private const uint WinModifier = 0x0008;

    private const uint NoRepeatModifier = 0x4000;

    private readonly Dictionary<int, Hotkey> _bindings = [];

    private readonly TopLevel _anchor;

    private readonly IntPtr _handle;

    private readonly Action<Hotkey> _launch;

    private readonly Win32Properties.CustomWndProcHookCallback _hook;

    private WindowsGlobalHotkeys(TopLevel anchor, IntPtr handle, Action<Hotkey> launch)
    {
        _anchor = anchor;
        _handle = handle;
        _launch = launch;
        _hook = OnMessage;
    }

    public static WindowsGlobalHotkeys? TryStart(
        IReadOnlyList<Hotkey> hotkeys, TopLevel anchor, Action<Hotkey> launch)
    {
        if (anchor.TryGetPlatformHandle() is not { } handle)
        {
            BasinLog.Warn($"the anchor window has no Win32 handle, global hotkeys are off");
            return null;
        }

        var instance = new WindowsGlobalHotkeys(anchor, handle.Handle, launch);
        var next = 1;
        foreach (var hotkey in hotkeys)
        {
            if (VirtualKey(hotkey.Key) is not { } key)
            {
                BasinLog.Warn($"hotkey '{hotkey.Chord}': no Windows key code for '{hotkey.Key}', skipping");
                continue;
            }

            if (!RegisterHotKey(handle.Handle, next, WindowsModifiers(hotkey.Modifiers) | NoRepeatModifier, key))
            {
                BasinLog.Warn($"hotkey '{hotkey.Chord}' was refused ({Marshal.GetLastPInvokeError()}), skipping");
                continue;
            }

            instance._bindings[next] = hotkey;
            next++;
        }

        if (instance._bindings.Count == 0)
        {
            return null;
        }

        Win32Properties.AddWndProcHookCallback(anchor, instance._hook);
        BasinLog.Debug($"{instance._bindings.Count} global hotkey(s) registered");
        return instance;
    }

    public void Dispose()
    {
        Win32Properties.RemoveWndProcHookCallback(_anchor, _hook);
        foreach (var id in _bindings.Keys)
        {
            _ = UnregisterHotKey(_handle, id);
        }

        _bindings.Clear();
    }

    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && _bindings.TryGetValue((int)wParam, out var hotkey))
        {
            try
            {
                _launch(hotkey);
            }
            catch (Exception error)
            {
                BasinLog.Warn($"hotkey dispatch failed: {error.Message}");
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    private static uint WindowsModifiers(HotkeyModifiers modifiers)
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
            value |= AltModifier;
        }

        if ((modifiers & HotkeyModifiers.Super) != 0)
        {
            value |= WinModifier;
        }

        return value;
    }

    private static uint? VirtualKey(string key) => key switch
    {
        { Length: 1 } when key[0] is >= 'a' and <= 'z' => (uint)(key[0] - 'a') + 0x41,
        { Length: 1 } when key[0] is >= '0' and <= '9' => (uint)(key[0] - '0') + 0x30,
        "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73, "f5" => 0x74, "f6" => 0x75,
        "f7" => 0x76, "f8" => 0x77, "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
        "space" => 0x20, "enter" or "return" => 0x0D, "tab" => 0x09, "escape" => 0x1B,
        "backspace" => 0x08, "delete" => 0x2E, "insert" => 0x2D,
        "home" => 0x24, "end" => 0x23, "pageup" => 0x21, "pagedown" => 0x22,
        "left" => 0x25, "up" => 0x26, "right" => 0x27, "down" => 0x28,
        "minus" => 0xBD, "equal" => 0xBB, "comma" => 0xBC, "period" => 0xBE,
        "slash" => 0xBF, "backslash" => 0xDC, "semicolon" => 0xBA, "apostrophe" => 0xDE,
        "grave" => 0xC0, "bracketleft" => 0xDB, "bracketright" => 0xDD,
        _ => null,
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
