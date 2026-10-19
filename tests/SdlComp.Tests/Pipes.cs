using System.Runtime.InteropServices;
using System.Text;

namespace SdlComp.Tests;

internal static unsafe class Pipes
{
    [DllImport("libc", SetLastError = true)]
    private static extern int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    public static extern int close(int fd);

    public static (int Read, int Write) Open()
    {
        var fds = stackalloc int[2];
        if (pipe2(fds, 0x800 | 0x80000) != 0)
        {
            throw new InvalidOperationException("pipe2 failed");
        }

        return (fds[0], fds[1]);
    }

    public static void WriteText(int fd, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* data = bytes)
        {
            _ = write(fd, data, (nuint)bytes.Length);
        }

        _ = close(fd);
    }

    public static string? ReadText(int fd, Action pump, int attempts = 400)
    {
        var received = new List<byte>();
        var buffer = new byte[4096];
        try
        {
            for (var i = 0; i < attempts; i++)
            {
                nint got;
                fixed (byte* data = buffer)
                {
                    got = read(fd, data, (nuint)buffer.Length);
                }

                if (got == 0)
                {
                    return Encoding.UTF8.GetString([.. received]);
                }

                if (got > 0)
                {
                    received.AddRange(buffer.AsSpan(0, (int)got).ToArray());
                    continue;
                }

                pump();
                Thread.Sleep(2);
            }

            return null;
        }
        finally
        {
            _ = close(fd);
        }
    }
}
