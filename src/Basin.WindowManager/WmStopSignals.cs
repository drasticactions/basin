using System.Runtime.InteropServices;

namespace Basin.WindowManager;

internal sealed class WmStopSignals : IDisposable
{
    private readonly PosixSignalRegistration _interrupt;
    private readonly PosixSignalRegistration _terminate;
    private readonly IWmEventSource _source;
    private readonly int _readFd;
    private readonly int _writeFd;
    private bool _disposed;

    private WmStopSignals(WmEventLoop loop, int readFd, int writeFd, Action<int> received)
    {
        _readFd = readFd;
        _writeFd = writeFd;
        _interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Forward);
        _terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Forward);
        var count = 0;
        _source = loop.AddFd(_readFd, WmFdReadiness.Readable, (fd, _) =>
        {
            unsafe
            {
                var buffer = stackalloc byte[16];
                nint read;
                while ((read = WmStopSignals.read(fd, buffer, 16)) > 0)
                {
                    count += (int)read;
                }
            }

            received(count);
        });
    }

    public static WmStopSignals? TryCreate(WmEventLoop loop, Action<int> received)
    {
        Span<int> fds = stackalloc int[2];
        unsafe
        {
            fixed (int* pair = fds)
            {
                if (pipe2(pair, 0x80000 | 0x800) != 0)
                {
                    return null;
                }
            }
        }

        return new WmStopSignals(loop, fds[0], fds[1], received);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Remove();
        _interrupt.Dispose();
        _terminate.Dispose();
        _ = close(_readFd);
        _ = close(_writeFd);
    }

    private void Forward(PosixSignalContext context)
    {
        context.Cancel = true;
        unsafe
        {
            byte token = 1;
            _ = write(_writeFd, &token, 1);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
