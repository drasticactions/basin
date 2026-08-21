using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

public sealed class WaylandKeyboardDevice
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int munmap(void* addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);

    internal WaylandKeyboardDevice(WaylandBackend backend, WlKeyboard keyboard)
    {
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
        };
        keyboard.Leave += (_, _) => Leave?.Invoke();
        keyboard.Key += (_, e) =>
        {
            backend.LastKeyboardSerial = e.Serial;
            Key?.Invoke(e.Time, e.Key, e.State == WlKeyboard.KeyState.Pressed);
        };
        keyboard.Modifiers += (_, e) => Modifiers?.Invoke(e.ModsDepressed, e.ModsLatched, e.ModsLocked, e.Group);
        keyboard.RepeatInfo += (_, e) => RepeatInfo?.Invoke(e.Rate, e.Delay);
    }

    public event Action<int, int>? RepeatInfo;

    public event Action<byte[]>? Keymap;

    public event Action<WaylandOutput>? Enter;

    public event Action? Leave;

    public event Action<uint, uint, bool>? Key;

    public event Action<uint, uint, uint, uint>? Modifiers;

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
