using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Basin.Transport.Waypipe;

[SupportedOSPlatform("linux")]
internal static unsafe partial class VsockNative
{
    internal const int EIntr = 4;
    internal const int EAgain = 11;
    internal const int EAcces = 13;
    internal const int ENFile = 23;
    internal const int EMFile = 24;
    internal const int EAfNoSupport = 97;
    internal const int EConnAborted = 103;

    private const int SockStream = 1;
    private const int SockNonblock = 0x800;
    private const int SockCloexec = 0x80000;

    internal static int LastError => Marshal.GetLastPInvokeError();

    internal static int OpenListening(VsockEndPoint endpoint, int backlog)
    {
        var fd = socket((int)VsockEndPoint.VsockFamily, SockStream | SockNonblock | SockCloexec, 0);
        if (fd < 0)
        {
            var errno = LastError;
            throw errno == EAfNoSupport
                ? new IOException(
                    $"socket(AF_VSOCK) for {endpoint} failed because no vsock transport is loaded. "
                    + "Load vhost_vsock on a host, vmw_vsock_virtio_transport in a guest, hv_sock in a Hyper-V guest, or vsock_loopback on one machine.")
                : Failure("socket", endpoint, errno);
        }

        try
        {
            var address = stackalloc byte[VsockEndPoint.SockaddrLength];
            endpoint.Write(new Span<byte>(address, VsockEndPoint.SockaddrLength));
            if (bind(fd, address, VsockEndPoint.SockaddrLength) != 0)
            {
                var errno = LastError;
                throw errno == EAcces && endpoint.Port < 1024
                    ? new IOException(
                        $"bind({endpoint}) failed because vsock ports below 1024 need CAP_NET_BIND_SERVICE. Choose port 1024 or higher.")
                    : Failure("bind", endpoint, errno);
            }

            if (listen(fd, backlog) != 0)
            {
                throw Failure("listen", endpoint, LastError);
            }

            return fd;
        }
        catch
        {
            _ = close(fd);
            throw;
        }
    }

    internal static VsockEndPoint LocalEndPoint(int fd, VsockEndPoint endpoint)
    {
        var address = stackalloc byte[VsockEndPoint.SockaddrLength];
        var length = VsockEndPoint.SockaddrLength;
        if (getsockname(fd, address, &length) != 0)
        {
            throw Failure("getsockname", endpoint, LastError);
        }

        return VsockEndPoint.Read(new ReadOnlySpan<byte>(address, VsockEndPoint.SockaddrLength));
    }

    internal static int Accept(int fd) => accept4(fd, 0, 0, SockCloexec);

    internal static int Connect(VsockEndPoint endpoint)
    {
        var fd = socket((int)VsockEndPoint.VsockFamily, SockStream | SockCloexec, 0);
        if (fd < 0)
        {
            throw Failure("socket", endpoint, LastError);
        }

        var address = stackalloc byte[VsockEndPoint.SockaddrLength];
        endpoint.Write(new Span<byte>(address, VsockEndPoint.SockaddrLength));
        if (connect(fd, address, VsockEndPoint.SockaddrLength) != 0)
        {
            var errno = LastError;
            _ = close(fd);
            throw Failure("connect", endpoint, errno);
        }

        return fd;
    }

    internal static int Close(int fd) => close(fd);

    private static IOException Failure(string call, VsockEndPoint endpoint, int errno) =>
        new($"{call}({endpoint}) failed with errno {errno}: {Marshal.GetPInvokeErrorMessage(errno)}.");

    [LibraryImport("libc", SetLastError = true)]
    private static partial int socket(int domain, int type, int protocol);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int bind(int fd, byte* address, int length);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int connect(int fd, byte* address, int length);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int listen(int fd, int backlog);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int accept4(int fd, nint address, nint length, int flags);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int getsockname(int fd, byte* address, int* length);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int close(int fd);
}
