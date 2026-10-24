using System.Runtime.InteropServices;

namespace Basin.WindowManager;

/// <summary>
/// Turns SIGHUP into a <see cref="Reload"/> event on the loop thread. The signal handler only writes to a pipe, and the loop asks for a manage sequence.
/// </summary>
public sealed class WmReloadSignal : IDisposable
{
    private readonly RiverWindowManager _wm;
    private readonly PosixSignalRegistration? _registration;
    private readonly IWmEventSource? _source;
    private readonly int _readFd = -1;
    private readonly int _writeFd = -1;
    private bool _pending;

    /// <summary>
    /// Initializes a new instance of the <see cref="WmReloadSignal"/> class. It handles SIGHUP in place of the default action. When the pipe cannot be made, the instance does nothing.
    /// </summary>
    /// <param name="wm">The manager whose loop watches the pipe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
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
                    return;
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
    /// Removes the fd watch, restores the signal's default handling and closes the pipe.
    /// </summary>
    public void Dispose()
    {
        _source?.Remove();
        _registration?.Dispose();
        if (_readFd >= 0)
        {
            _ = close(_readFd);
        }

        if (_writeFd >= 0)
        {
            _ = close(_writeFd);
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
