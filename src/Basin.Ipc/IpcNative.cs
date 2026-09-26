using System.Runtime.InteropServices;

namespace Basin.Ipc;

internal static class IpcNative
{
    [DllImport("libc", EntryPoint = "setenv", SetLastError = true)]
    private static extern int SetEnv([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);

    [DllImport("libc", EntryPoint = "unsetenv", SetLastError = true)]
    private static extern int UnsetEnv([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    private const int OCloexec = 0x80000;
    private const int ONonblock = 0x800;
    private const int FSetfl = 4;

    [DllImport("libc", EntryPoint = "pipe2", SetLastError = true)]
    private static extern unsafe int Pipe2(int* fds, int flags);

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int command, int argument);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern unsafe nint ReadRaw(int fd, byte* buffer, nuint count);

    public static unsafe bool TryPipe(out int read, out int write)
    {
        var fds = stackalloc int[2];
        if (Pipe2(fds, OCloexec) != 0)
        {
            read = write = -1;
            return false;
        }

        read = fds[0];
        write = fds[1];
        if (Fcntl(read, FSetfl, ONonblock) != 0)
        {
            _ = UnixSocket.Close(read);
            _ = UnixSocket.Close(write);
            read = write = -1;
            return false;
        }

        return true;
    }

    public static unsafe nint Read(int fd, Span<byte> buffer)
    {
        fixed (byte* data = buffer)
        {
            return ReadRaw(fd, data, (nuint)buffer.Length);
        }
    }

    public static void Export(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _ = value is null ? UnsetEnv(name) : SetEnv(name, value, 1);
    }
}
