using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Basin.Tests;

internal static class VsockFacts
{
    private const int AfVsock = 40;
    private const int SockStream = 1;
    private const int SockCloexec = 0x80000;
    private const uint CidAny = 0xFFFFFFFF;
    private const uint CidLocal = 1;
    private const uint PortAny = 0xFFFFFFFF;

    private static readonly Lazy<string?> Loopback = new(Probe);

    public static string? LoopbackReason() => Loopback.Value;

    public static bool IsWsl { get; } =
        OperatingSystem.IsLinux() &&
        (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")) ||
            File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop") ||
            File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop-late"));

    public static string? WaypipeReason() =>
        LoopbackReason() ?? (OnPath("waypipe") is null ? "waypipe is not on PATH" : null);

    public static string? OnPath(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':'))
        {
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name)))
            {
                return Path.Combine(directory, name);
            }
        }

        return null;
    }

    private static string? Probe()
    {
        if (!OperatingSystem.IsLinux())
        {
            return "vsock is Linux only";
        }

        if (!File.Exists("/dev/vsock"))
        {
            return "/dev/vsock is absent; run sudo modprobe vsock_loopback";
        }

        var listener = socket(AfVsock, SockStream | SockCloexec, 0);
        if (listener < 0)
        {
            return $"socket(AF_VSOCK) failed with errno {Marshal.GetLastPInvokeError()}";
        }

        var client = -1;
        try
        {
            var address = Address(CidAny, PortAny);
            if (bind(listener, address, address.Length) != 0 || listen(listener, 1) != 0)
            {
                return $"a vsock listener could not bind, errno {Marshal.GetLastPInvokeError()}";
            }

            var length = address.Length;
            if (getsockname(listener, address, ref length) != 0)
            {
                return $"getsockname on a vsock listener failed, errno {Marshal.GetLastPInvokeError()}";
            }

            var port = BinaryPrimitives.ReadUInt32LittleEndian(address.AsSpan(4));
            client = socket(AfVsock, SockStream | SockCloexec, 0);
            var target = Address(CidLocal, port);
            if (client < 0 || connect(client, target, target.Length) != 0)
            {
                return $"CID 1 does not reach a bound vsock port (errno {Marshal.GetLastPInvokeError()}); run sudo modprobe vsock_loopback";
            }

            return null;
        }
        finally
        {
            if (client >= 0)
            {
                _ = close(client);
            }

            _ = close(listener);
        }
    }

    private static byte[] Address(uint cid, uint port)
    {
        var address = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(address, AfVsock);
        BinaryPrimitives.WriteUInt32LittleEndian(address.AsSpan(4), port);
        BinaryPrimitives.WriteUInt32LittleEndian(address.AsSpan(8), cid);
        return address;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    private static extern int bind(int fd, byte[] address, int length);

    [DllImport("libc", SetLastError = true)]
    private static extern int connect(int fd, byte[] address, int length);

    [DllImport("libc", SetLastError = true)]
    private static extern int listen(int fd, int backlog);

    [DllImport("libc", SetLastError = true)]
    private static extern int getsockname(int fd, byte[] address, ref int length);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
