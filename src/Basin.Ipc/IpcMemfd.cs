using System.Runtime.InteropServices;

namespace Basin.Ipc;

internal static unsafe class IpcMemfd
{
    private const uint Cloexec = 1;

    public static int Create(string name, ReadOnlySpan<byte> contents)
    {
        var fd = memfd_create(name, Cloexec);
        if (fd < 0)
        {
            return -1;
        }

        fixed (byte* data = contents)
        {
            var offset = 0;
            while (offset < contents.Length)
            {
                var written = write(fd, data + offset, (nuint)(contents.Length - offset));
                if (written < 0)
                {
                    if (Marshal.GetLastPInvokeError() == UnixSocket.EIntr)
                    {
                        continue;
                    }

                    _ = UnixSocket.Close(fd);
                    return -1;
                }

                offset += (int)written;
            }
        }

        return fd;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create([MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte* data, nuint count);
}
