using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

/// <summary>
/// Reports the parent seat's keyboard. It releases every held key on enter and on leave, so a consumer never sees a press without a release.
/// </summary>
public sealed class WaylandKeyboardDevice
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int munmap(void* addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly List<uint> _held = [];

    private readonly WlKeyboard _keyboard;

    internal WaylandKeyboardDevice(WaylandBackend backend, WlKeyboard keyboard)
    {
        _keyboard = keyboard;
        keyboard.Keymap += (_, e) =>
        {
            var bytes = ReadKeymap(e.Fd, e.Size);
            backend.ParentDisplay?.CloseFd(e.Fd);
            if (bytes is not null)
            {
                Keymap?.Invoke(bytes);
            }
        };
        keyboard.Enter += (_, e) =>
        {
            backend.LastKeyboardSerial = e.Serial;
            var output = backend.FindOutput(e.Surface);
            if (output is not null)
            {
                Enter?.Invoke(output);
            }

            ReleaseHeld((uint)Environment.TickCount64);
        };
        keyboard.Leave += (_, _) =>
        {
            ReleaseHeld((uint)Environment.TickCount64);
            Leave?.Invoke();
        };
        keyboard.Key += (_, e) =>
        {
            backend.LastKeyboardSerial = e.Serial;
            var pressed = e.State == WlKeyboard.KeyState.Pressed;
            if (pressed)
            {
                if (!_held.Contains(e.Key))
                {
                    _held.Add(e.Key);
                }
            }
            else if (!_held.Remove(e.Key))
            {
                return;
            }

            Key?.Invoke(e.Time, e.Key, pressed);
        };
        keyboard.Modifiers += (_, e) => Modifiers?.Invoke(e.ModsDepressed, e.ModsLatched, e.ModsLocked, e.Group);
        keyboard.RepeatInfo += (_, e) => RepeatInfo?.Invoke(e.Rate, e.Delay);
    }

    /// <summary>Occurs when the parent sends its key repeat settings. The arguments are the rate in keys per second and the delay in milliseconds.</summary>
    public event Action<int, int>? RepeatInfo;

    /// <summary>
    /// Occurs when the parent sends a keymap. The array is the XKB text keymap without its terminating zero. The handler owns the array.
    /// </summary>
    public event Action<byte[]>? Keymap;

    /// <summary>
    /// Occurs when an output gets the parent's keyboard focus. Keys that were held before the enter are not reported.
    /// </summary>
    public event Action<WaylandOutput>? Enter;

    /// <summary>
    /// Occurs when the parent's keyboard focus leaves a surface, or when the parent removes the keyboard. Release events for held keys come first.
    /// </summary>
    public event Action? Leave;

    /// <summary>
    /// Occurs when a key changes state. The arguments are the time in milliseconds, the Linux input event code and the pressed state.
    /// A release for a key that this device did not see pressed is dropped.
    /// </summary>
    public event Action<uint, uint, bool>? Key;

    /// <summary>
    /// Occurs when the parent's modifier state changes. The arguments are the depressed, latched and locked masks and the layout group.
    /// </summary>
    public event Action<uint, uint, uint, uint>? Modifiers;

    internal void ReleaseHeld(uint timeMs)
    {
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            var key = _held[i];
            _held.RemoveAt(i);
            Key?.Invoke(timeMs, key, false);
        }
    }

    internal void Remove(uint timeMs)
    {
        ReleaseHeld(timeMs);
        Leave?.Invoke();
        WaylandBackend.ReleaseInputProxy(_keyboard);
    }

    private static unsafe byte[]? ReadKeymap(int fd, uint size)
    {
        var map = mmap(null, size, 1 , 2 , fd, 0);
        if ((nint)map == -1)
        {
            return null;
        }

        var span = new ReadOnlySpan<byte>(map, (int)size);
        var length = span.IndexOf((byte)0);
        var bytes = span[..(length < 0 ? (int)size : length)].ToArray();
        munmap(map, size);
        return bytes;
    }
}
