using System.Runtime.InteropServices;

namespace Basin.Hosted;

internal static partial class HeadlessNative
{
    public const int EfdCloexec = 0x80000;

    public const int EfdNonblock = 0x800;

    [LibraryImport("libc", EntryPoint = "eventfd", SetLastError = true)]
    public static partial int EventFd(uint initial, int flags);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    public static unsafe partial nint Read(int fd, void* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    public static unsafe partial nint Write(int fd, void* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    public static partial int Close(int fd);
}
