using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Basin.Seat;

[SupportedOSPlatform("windows")]
public sealed class WindowsKeyboardLayout : IHostKeyboardLayout, IDisposable
{
    private readonly Timer _poll;
    private nint _layout;

    private WindowsKeyboardLayout(nint layout)
    {
        _layout = layout;
        Name = Describe(layout);
        _poll = new Timer(_ => Poll(), null, PollMillis, PollMillis);
    }

    public const int PollMillis = 500;

    public string Name { get; private set; }

    public event Action? Changed;

    public static WindowsKeyboardLayout? TryCreate()
    {
        var layout = CurrentLayout();
        return layout == 0 ? null : new WindowsKeyboardLayout(layout);
    }

    public bool TryReadKeymapText(out string xkb)
    {
        var levels = new List<HostKeymapWriter.Levels>();
        var state = new byte[256];

        foreach (var (code, evdev) in HostKeyMap.Entries)
        {
            if (!HostKeymapWriter.TryKeycodeName(code, out _))
            {
                continue;
            }

            var scan = ScanCodeOf(evdev);
            if (scan == 0)
            {
                continue;
            }

            var virtualKey = MapVirtualKeyEx(scan, MapvkVscToVk, _layout);
            if (virtualKey == 0)
            {
                continue;
            }

            var plain = Translate(virtualKey, scan, state, shift: false, altGr: false);
            if (plain is null)
            {
                continue;
            }

            levels.Add(new HostKeymapWriter.Levels(
                code,
                plain,
                Translate(virtualKey, scan, state, shift: true, altGr: false),
                Translate(virtualKey, scan, state, shift: false, altGr: true),
                Translate(virtualKey, scan, state, shift: true, altGr: true)));
        }

        if (levels.Count == 0)
        {
            xkb = string.Empty;
            return false;
        }

        xkb = HostKeymapWriter.Write(Name, levels);
        return true;
    }

    public void Dispose() => _poll.Dispose();

    private void Poll()
    {
        var current = CurrentLayout();
        if (current == 0 || current == _layout)
        {
            return;
        }

        _layout = current;
        Name = Describe(current);
        Changed?.Invoke();
    }

    private string? Translate(uint virtualKey, uint scan, byte[] state, bool shift, bool altGr)
    {
        Array.Clear(state);
        if (shift)
        {
            state[VkShift] = 0x80;
        }

        if (altGr)
        {
            state[VkControl] = 0x80;
            state[VkMenu] = 0x80;
        }

        var buffer = new StringBuilder(8);
        var written = ToUnicodeEx(virtualKey, scan, state, buffer, buffer.Capacity, 0, _layout);
        if (written == 0)
        {
            return null;
        }

        if (written < 0)
        {
            var dead = buffer.ToString(0, 1);
            _ = ToUnicodeEx(VkSpace, ScanSpace, new byte[256], new StringBuilder(8), 8, 0, _layout);
            return HostKeymapWriter.DeadKeysymName(dead[0]);
        }

        var text = buffer.ToString(0, written);
        return text.Length == 1 ? HostKeymapWriter.KeysymName(text[0]) : null;
    }

    private static uint ScanCodeOf(uint evdev) => evdev switch
    {
        <= 83 => evdev,
        86 => 86,
        87 => 87,
        88 => 88,
        89 => 0x73,
        _ => 0,
    };

    private static string Describe(nint layout)
    {
        var id = (uint)layout.ToInt64() & 0xffff;
        var name = new StringBuilder(9);
        return GetKeyboardLayoutNameW(name) && name.Length > 0
            ? name.ToString()
            : id.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static nint CurrentLayout()
    {
        var window = GetForegroundWindow();
        var thread = window == 0 ? 0 : GetWindowThreadProcessId(window, 0);
        return GetKeyboardLayout(thread);
    }

    private const uint MapvkVscToVk = 1;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const uint VkSpace = 0x20;
    private const uint ScanSpace = 57;

    [DllImport("user32", SetLastError = true)]
    private static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32", SetLastError = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, nint processId);

    [DllImport("user32", SetLastError = true)]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ToUnicodeEx(
        uint virtualKey, uint scanCode, byte[] state, StringBuilder buffer, int size, uint flags, nint layout);

    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "GetKeyboardLayoutNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardLayoutNameW(StringBuilder name);
}
