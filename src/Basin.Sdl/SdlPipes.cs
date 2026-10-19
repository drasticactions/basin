using System.Runtime.InteropServices;

namespace Basin.Sdl;

internal static unsafe class SdlPipes
{
    private const int OCloexec = 0x80000;
    private const short PollIn = 1;

    public static bool Open(out int readFd, out int writeFd)
    {
        readFd = writeFd = -1;
        if (!PlatformFacts.HasDescriptors)
        {
            return false;
        }

        var fds = stackalloc int[2];
        var made = OperatingSystem.IsLinux() ? pipe2(fds, OCloexec) : pipe(fds);
        if (made != 0)
        {
            return false;
        }

        readFd = fds[0];
        writeFd = fds[1];
        return true;
    }

    public static byte[]? ReadAll(int fd, int maxBytes, TimeSpan timeout)
    {
        try
        {
            using var stream = new MemoryStream();
            var buffer = new byte[16384];
            var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (true)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                {
                    return null;
                }

                var poll = new PollFd { Fd = fd, Events = PollIn };
                var ready = SdlPipes.poll(&poll, 1, (int)remaining);
                if (ready < 0 && Marshal.GetLastPInvokeError() == 4)
                {
                    continue;
                }

                if (ready <= 0)
                {
                    return null;
                }

                nint got;
                fixed (byte* data = buffer)
                {
                    got = read(fd, data, (nuint)buffer.Length);
                }

                if (got < 0)
                {
                    return null;
                }

                if (got == 0)
                {
                    return stream.ToArray();
                }

                if (stream.Length + got > maxBytes)
                {
                    return null;
                }

                stream.Write(buffer, 0, (int)got);
            }
        }
        finally
        {
            _ = close(fd);
        }
    }

    public static void WriteAll(int fd, byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            nint wrote;
            fixed (byte* data = bytes)
            {
                wrote = write(fd, data + offset, (nuint)(bytes.Length - offset));
            }

            if (wrote <= 0)
            {
                break;
            }

            offset += (int)wrote;
        }
    }

    public static void Close(int fd) => _ = close(fd);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int pipe(int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(PollFd* fds, nuint count, int timeoutMs);

    [DllImport("libc")]
    private static extern nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);
}
