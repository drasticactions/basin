using System.Runtime.InteropServices;

namespace Basin.WindowManager;

/// <summary>
/// Turns SIGHUP into a <see cref="Reload"/> event on the loop thread. The signal handler only writes to a pipe, and the loop asks for a manage sequence.
/// </summary>
public sealed class WmReloadSignal : IDisposable
{
    private readonly RiverWindowManager _wm;
    private readonly PosixSignalRegistration _registration;
    private readonly IWmEventSource _source;
    private readonly int _readFd;
    private readonly int _writeFd;
    private bool _pending;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WmReloadSignal"/> class. It handles SIGHUP in place of the default action, which ends the process.
    /// </summary>
    /// <param name="wm">The manager whose loop watches the pipe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">The pipe cannot be made. SIGHUP then keeps its default action.</exception>
    public WmReloadSignal(RiverWindowManager wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        _wm = wm;

        Span<int> fds = stackalloc int[2];
        unsafe
        {
            fixed (int* pair = fds)
            {
                if (pipe2(pair, 0x80000 | 0x800) != 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    throw new IOException(
                        $"The SIGHUP reload pipe cannot be made: {Marshal.GetPInvokeErrorMessage(errno)} (errno {errno}).");
                }
            }
        }

        _readFd = fds[0];
        _writeFd = fds[1];
        var writeFd = _writeFd;

        _registration = PosixSignalRegistration.Create(
            PosixSignal.SIGHUP,
            context =>
            {
                context.Cancel = true;
                unsafe
                {
                    byte token = 1;
                    _ = write(writeFd, &token, 1);
                }
            });

        _source = _wm.Loop.AddFd(_readFd, WmFdReadiness.Readable, (fd, _) =>
        {
            unsafe
            {
                var buffer = stackalloc byte[16];
                while (read(fd, buffer, 16) > 0)
                {
                }
            }

            _pending = true;
            _wm.RequestManage();
        });
    }

    /// <summary>
    /// Occurs in <see cref="Process"/> when a SIGHUP arrived since the last call. Several signals in that time give one event.
    /// </summary>
    public event Action? Reload;

    /// <summary>
    /// Raises <see cref="Reload"/> when a SIGHUP is pending. A manager calls it from its manage callback.
    /// </summary>
    public void Process()
    {
        if (!_pending)
        {
            return;
        }

        _pending = false;
        Reload?.Invoke();
    }

    /// <summary>
    /// Removes the fd watch, restores the signal's default handling and closes the pipe. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Remove();
        _registration.Dispose();
        _ = close(_readFd);
        _ = close(_writeFd);
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
