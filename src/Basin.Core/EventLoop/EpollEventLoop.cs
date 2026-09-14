using System.Runtime.InteropServices;
using Basin.Diagnostics;

namespace Basin;

public sealed unsafe class EpollEventLoop : ICompositorEventLoop, IDisposable
{
    private const int EpollCloexec = 0x80000;
    private const int EpollCtlAdd = 1;
    private const int EpollCtlDel = 2;
    private const int EpollCtlMod = 3;
    private const uint EpollIn = 0x001;
    private const uint EpollOut = 0x004;
    private const uint EpollErr = 0x008;
    private const uint EpollHup = 0x010;
    private const int ClockMonotonic = 1;
    private const int TfdNonblock = 0x800;
    private const int TfdCloexec = 0x80000;
    private const int ONonblock = 0x800;
    private const int OCloexec = 0x80000;
    private const int MaxEvents = 32;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<ulong, Source> _sources = [];
    private readonly List<Action> _idle = [];
    private readonly List<Action> _idleRunning = [];
    private readonly List<IDisposable> _deferredFrees = [];
    private readonly List<IDisposable> _deferredFreesRunning = [];
    private readonly byte[] _events;
    private readonly int _eventSize;
    private readonly int _dataOffset;
    private int _epoll;
    private ulong _nextId;
    private bool _disposed;

    public EpollEventLoop()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("EpollEventLoop needs Linux");
        }

        _epoll = epoll_create1(EpollCloexec);
        if (_epoll < 0)
        {
            throw new InvalidOperationException($"epoll_create1 failed: errno {Marshal.GetLastPInvokeError()}");
        }

        var packed = RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86;
        _eventSize = packed ? 12 : 16;
        _dataOffset = packed ? 4 : 8;
        _events = new byte[_eventSize * MaxEvents];
    }

    public int Fd => _epoll;

    public void Dispatch(int timeoutMs)
    {
        _thread.Assert();
        DrainQueues();
        int count;
        fixed (byte* events = _events)
        {
            count = epoll_wait(_epoll, events, MaxEvents, timeoutMs);
        }

        if (count <= 0)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            uint flags;
            ulong id;
            fixed (byte* events = _events)
            {
                var entry = events + (i * _eventSize);
                flags = *(uint*)entry;
                id = *(ulong*)(entry + _dataOffset);
            }

            if (!_sources.TryGetValue(id, out var source) || source.IsRemoved)
            {
                continue;
            }

            source.Fire(flags);
        }
    }

    public IEventSource AddFd(int fd, FdReadiness events, Action<int, FdReadiness> handler)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(handler);
        var source = new FdSource(this, ++_nextId, fd, handler);
        Register(source, fd, ToEpoll(events));
        return source;
    }

    public IEventSource AddTimer(Action handler)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(handler);
        var fd = timerfd_create(ClockMonotonic, TfdNonblock | TfdCloexec);
        if (fd < 0)
        {
            throw new InvalidOperationException($"timerfd_create failed: errno {Marshal.GetLastPInvokeError()}");
        }

        var source = new TimerSource(this, ++_nextId, fd, handler);
        Register(source, fd, EpollIn);
        return source;
    }

    public IEventSource AddSignal(int signalNumber, Action<int> handler)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(handler);
        var fds = stackalloc int[2];
        if (pipe2(fds, ONonblock | OCloexec) != 0)
        {
            throw new InvalidOperationException($"pipe2 failed for signal {signalNumber}: errno {Marshal.GetLastPInvokeError()}");
        }

        var source = new SignalSource(this, ++_nextId, fds[0], fds[1], signalNumber, handler);
        Register(source, fds[0], EpollIn);
        return source;
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

    public void DeferDestroy(IDisposable victim)
    {
        _thread.Assert();
        _deferredFrees.Add(victim);
        BasinCounters.TrackPendingFree();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        _disposed = true;
        DrainQueues();
        foreach (var source in _sources.Values.ToArray())
        {
            source.Remove();
        }

        _sources.Clear();
        _ = close(_epoll);
        _epoll = -1;
    }

    private void Register(Source source, int fd, uint flags)
    {
        var entry = stackalloc byte[16];
        *(uint*)entry = flags;
        *(ulong*)(entry + _dataOffset) = source.Id;
        if (epoll_ctl(_epoll, EpollCtlAdd, fd, entry) != 0)
        {
            source.Close();
            throw new InvalidOperationException($"epoll_ctl failed for fd {fd}: errno {Marshal.GetLastPInvokeError()}");
        }

        _sources[source.Id] = source;
        BasinCounters.Track();
    }

    private void Modify(Source source, int fd, uint flags)
    {
        var entry = stackalloc byte[16];
        *(uint*)entry = flags;
        *(ulong*)(entry + _dataOffset) = source.Id;
        _ = epoll_ctl(_epoll, EpollCtlMod, fd, entry);
    }

    private void Unregister(Source source, int fd)
    {
        if (_sources.Remove(source.Id) && _epoll >= 0)
        {
            var entry = stackalloc byte[16];
            _ = epoll_ctl(_epoll, EpollCtlDel, fd, entry);
            BasinCounters.Untrack();
        }
    }

    private static uint ToEpoll(FdReadiness events)
    {
        uint flags = 0;
        if ((events & FdReadiness.Readable) != 0)
        {
            flags |= EpollIn;
        }

        if ((events & FdReadiness.Writable) != 0)
        {
            flags |= EpollOut;
        }

        return flags;
    }

    private static FdReadiness FromEpoll(uint flags)
    {
        var events = FdReadiness.None;
        if ((flags & EpollIn) != 0)
        {
            events |= FdReadiness.Readable;
        }

        if ((flags & EpollOut) != 0)
        {
            events |= FdReadiness.Writable;
        }

        if ((flags & EpollHup) != 0)
        {
            events |= FdReadiness.Hangup;
        }

        if ((flags & EpollErr) != 0)
        {
            events |= FdReadiness.Error;
        }

        return events;
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

    private abstract class Source(EpollEventLoop owner, ulong id) : IEventSource
    {
        protected EpollEventLoop Owner { get; } = owner;

        public ulong Id { get; } = id;

        public bool IsRemoved { get; private set; }

        public abstract void Fire(uint flags);

        public virtual void UpdateTimer(int delayMs) =>
            throw new InvalidOperationException("this source has no timer");

        public virtual void UpdateFd(FdReadiness events) =>
            throw new InvalidOperationException("this source watches its own fd");

        public void Remove()
        {
            if (IsRemoved)
            {
                return;
            }

            IsRemoved = true;
            RemoveCore();
        }

        protected abstract void RemoveCore();

        public abstract void Close();
    }

    private sealed class FdSource(EpollEventLoop owner, ulong id, int fd, Action<int, FdReadiness> handler) : Source(owner, id)
    {
        public override void Fire(uint flags) => handler(fd, FromEpoll(flags));

        public override void UpdateFd(FdReadiness events) => Owner.Modify(this, fd, ToEpoll(events));

        protected override void RemoveCore() => Owner.Unregister(this, fd);

        public override void Close()
        {
        }
    }

    private sealed class TimerSource(EpollEventLoop owner, ulong id, int fd, Action handler) : Source(owner, id)
    {
        public override void Fire(uint flags)
        {
            ulong expirations;
            _ = read(fd, &expirations, 8);
            handler();
        }

        public override void UpdateTimer(int delayMs)
        {
            var spec = stackalloc long[4];
            if (delayMs > 0)
            {
                spec[2] = delayMs / 1000;
                spec[3] = (delayMs % 1000) * 1_000_000L;
            }

            _ = timerfd_settime(fd, 0, spec, null);
        }

        protected override void RemoveCore()
        {
            Owner.Unregister(this, fd);
            Close();
        }

        public override void Close() => _ = close(fd);
    }

    private sealed class SignalSource : Source
    {
        private readonly int _readFd;
        private readonly int _writeFd;
        private readonly int _signalNumber;
        private readonly Action<int> _handler;
        private readonly PosixSignalRegistration _registration;
        private readonly object _gate = new();
        private bool _closed;

        public SignalSource(EpollEventLoop owner, ulong id, int readFd, int writeFd, int signalNumber, Action<int> handler)
            : base(owner, id)
        {
            _readFd = readFd;
            _writeFd = writeFd;
            _signalNumber = signalNumber;
            _handler = handler;
            _registration = PosixSignalRegistration.Create((PosixSignal)signalNumber, context =>
            {
                context.Cancel = true;
                lock (_gate)
                {
                    if (!_closed)
                    {
                        byte one = 1;
                        _ = write(_writeFd, &one, 1);
                    }
                }
            });
        }

        public override void Fire(uint flags)
        {
            byte scratch;
            var pending = 0;
            while (read(_readFd, &scratch, 1) == 1)
            {
                pending++;
            }

            for (var i = 0; i < pending; i++)
            {
                _handler(_signalNumber);
            }
        }

        protected override void RemoveCore()
        {
            _registration.Dispose();
            Owner.Unregister(this, _readFd);
            Close();
        }

        public override void Close()
        {
            lock (_gate)
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
            }

            _ = close(_readFd);
            _ = close(_writeFd);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int epoll_create1(int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int epoll_ctl(int epoll, int operation, int fd, byte* entry);

    [DllImport("libc", SetLastError = true)]
    private static extern int epoll_wait(int epoll, byte* events, int maxEvents, int timeoutMs);

    [DllImport("libc", SetLastError = true)]
    private static extern int timerfd_create(int clock, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int timerfd_settime(int fd, int flags, long* newValue, long* oldValue);

    [DllImport("libc", SetLastError = true)]
    private static extern int pipe2(int* fds, int flags);

    [DllImport("libc")]
    private static extern nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern nint read(int fd, void* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);
}
