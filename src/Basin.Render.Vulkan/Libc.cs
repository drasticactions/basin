using System.Runtime.InteropServices;

namespace Basin.Render.Vulkan;

internal static unsafe class Libc
{
    public const int ORdwr = 2;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    public static extern int Open(string path, int flags);

    [DllImport("libc", EntryPoint = "close")]
    public static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "dup")]
    public static extern int Dup(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int stat(string path, byte* buf);

    [DllImport("libc", SetLastError = true)]
    private static extern int fstat(int fd, byte* buf);

    private const int StatSize = 160;

    private const int InodeOffset = 8;

    private static int RdevOffset => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => 40,
        Architecture.Arm64 => 32,
        var arch => throw new PlatformNotSupportedException($"struct stat layout unknown on {arch}"),
    };

    public static ulong RdevOf(string path)
    {
        var buf = stackalloc byte[StatSize];
        if (stat(path, buf) != 0)
        {
            throw new InvalidOperationException($"stat({path}) failed");
        }

        return *(ulong*)(buf + RdevOffset);
    }

    public static bool TryInodeOf(int fd, out ulong inode)
    {
        var buf = stackalloc byte[StatSize];
        if (fstat(fd, buf) != 0)
        {
            inode = 0;
            return false;
        }

        inode = *(ulong*)(buf + InodeOffset);
        return true;
    }
}
