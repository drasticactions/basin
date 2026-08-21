using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

public sealed class WaylandEventLoop : ICompositorEventLoop
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly WlServerDisplay _display;
    private readonly List<Action> _idle = [];
    private readonly List<Action> _idleRunning = [];
    private readonly List<IDisposable> _deferredFrees = [];
    private readonly List<IDisposable> _deferredFreesRunning = [];

    public WaylandEventLoop(WlServerDisplay display)
    {
        _display = display;
    }

    public void Dispatch(int timeoutMs)
    {
        _thread.Assert();
        DrainQueues();
        _display.FlushClients();
        _display.EventLoop.Dispatch(timeoutMs);
    }

    public IEventSource AddFd(int fd, FdReadiness events, Action<int, FdReadiness> handler)
    {
        _thread.Assert();
        var inner = _display.EventLoop.AddFd(fd, (WlFdEvents)events, (f, e) => handler(f, (FdReadiness)e));
        BasinCounters.Track();
        return new Source(inner);
    }

    public IEventSource AddTimer(Action handler)
    {
        _thread.Assert();
        var inner = _display.EventLoop.AddTimer(handler);
        BasinCounters.Track();
        return new Source(inner);
    }

    public IEventSource AddSignal(int signalNumber, Action<int> handler)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(handler);
        if (OperatingSystem.IsWindows())
        {
            var inner = _display.EventLoop.AddSignal(signalNumber, handler);
            BasinCounters.Track();
            return new Source(inner);
        }

        return new SignalSource(_display, signalNumber, handler);
    }

    public void AddIdle(Action handler)
    {
        _thread.Assert();
        _idle.Add(handler);
    }

    public void DispatchIdle()
    {
        _thread.Assert();
        DrainQueues();
    }

    public int Fd => _display.EventLoop.Fd;

    public void DeferDestroy(IDisposable victim)
    {
        _thread.Assert();
        _deferredFrees.Add(victim);
        BasinCounters.TrackPendingFree();
    }

    private void DrainQueues()
    {
        var hasIdle = _idle.Count > 0;
        var hasFrees = _deferredFrees.Count > 0;
        if (hasIdle)
        {
            _idleRunning.AddRange(_idle);
            _idle.Clear();
        }

        if (hasFrees)
        {
            _deferredFreesRunning.AddRange(_deferredFrees);
            _deferredFrees.Clear();
        }

        if (hasIdle)
        {
            foreach (var callback in _idleRunning)
            {
                callback();
            }

            _idleRunning.Clear();
        }

        if (hasFrees)
        {
            foreach (var victim in _deferredFreesRunning)
            {
                BasinCounters.UntrackPendingFree();
                victim.Dispose();
            }

            _deferredFreesRunning.Clear();
        }
    }

    private sealed class Source : IEventSource
    {
        private readonly WlEventSource _inner;

        internal Source(WlEventSource inner) => _inner = inner;

        public bool IsRemoved => _inner.IsRemoved;

        public void Remove()
        {
            if (!_inner.IsRemoved)
            {
                _inner.Remove();
                BasinCounters.Untrack();
            }
        }

        public void UpdateTimer(int delayMs) => _inner.UpdateTimer(delayMs);

        public void UpdateFd(FdReadiness events) => _inner.UpdateFd((WlFdEvents)events);
    }

    private sealed class SignalSource : IEventSource
    {
        private const int ONonblock = 0x800;
        private const int OCloexec = 0x80000;
        private const int ONonblockDarwin = 0x0004;
        private const int FGetFl = 3;
        private const int FSetFl = 4;
        private const int FSetFd = 2;
        private const int FdCloexec = 1;

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int pipe(int* fds);

        [DllImport("libc", SetLastError = true)]
        private static extern int fcntl(int fd, int command, int argument);

        private readonly PosixSignalRegistration _registration;
        private readonly WlEventSource _pipeSource;
        private readonly int _readFd;
        private readonly int _writeFd;
        private readonly object _gate = new();
        private bool _removed;

        internal unsafe SignalSource(WlServerDisplay display, int signalNumber, Action<int> handler)
        {
            var fds = stackalloc int[2];
            if (OperatingSystem.IsLinux())
            {
                if (pipe2(fds, ONonblock | OCloexec) != 0)
                {
                    throw new InvalidOperationException(
                        $"pipe2 failed for signal {signalNumber}: errno {Marshal.GetLastPInvokeError()}");
                }
            }
            else
            {
                if (pipe(fds) != 0)
                {
                    throw new InvalidOperationException(
                        $"pipe failed for signal {signalNumber}: errno {Marshal.GetLastPInvokeError()}");
                }

                for (var i = 0; i < 2; i++)
                {
                    _ = fcntl(fds[i], FSetFd, FdCloexec);
                    _ = fcntl(fds[i], FSetFl, fcntl(fds[i], FGetFl, 0) | ONonblockDarwin);
                }
            }

            _readFd = fds[0];
            _writeFd = fds[1];
            _registration = PosixSignalRegistration.Create((PosixSignal)signalNumber, context =>
            {
                context.Cancel = true;
                lock (_gate)
                {
                    if (!_removed)
                    {
                        WriteByte(_writeFd);
                    }
                }
            });
            _pipeSource = display.EventLoop.AddFd(_readFd, WlFdEvents.Readable, (_, _) =>
            {
                var pending = Drain(_readFd);
                for (var i = 0; i < pending; i++)
                {
                    handler(signalNumber);
                }
            });
            BasinCounters.Track();
        }

        public bool IsRemoved => _removed;

        public void Remove()
        {
            if (_removed)
            {
                return;
            }

            _registration.Dispose();
            lock (_gate)
            {
                _removed = true;
            }

            if (!_pipeSource.IsRemoved)
            {
                _pipeSource.Remove();
            }

            _ = close(_readFd);
            _ = close(_writeFd);
            BasinCounters.Untrack();
        }

        public void UpdateTimer(int delayMs) =>
            throw new InvalidOperationException("a signal source has no timer");

        public void UpdateFd(FdReadiness events) =>
            throw new InvalidOperationException("a signal source watches its own fd");

        private static unsafe void WriteByte(int fd)
        {
            byte one = 1;
            _ = write(fd, &one, 1);
        }

        private static unsafe int Drain(int fd)
        {
            byte scratch;
            var count = 0;
            while (read(fd, &scratch, 1) == 1)
            {
                count++;
            }

            return count;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int pipe2(int* fds, int flags);

        [DllImport("libc")]
        private static extern unsafe nint write(int fd, byte* buffer, nuint count);

        [DllImport("libc")]
        private static extern unsafe nint read(int fd, byte* buffer, nuint count);

        [DllImport("libc")]
        private static extern int close(int fd);
    }
}
