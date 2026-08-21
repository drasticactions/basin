using System.Runtime.InteropServices;

namespace Basin.WindowManager;

internal sealed class WmEventLoop : IWmEventLoop
{
    private readonly List<Source> _sources = [];
    private readonly List<Action> _idle = [];
    private readonly List<Action> _idleRunning = [];
    private PollFd[] _pollBuffer = [];
    private Source[] _pollOwners = [];
    private bool _dirty = true;

    public IWmEventSource AddFd(int fd, WmFdReadiness events, Action<int, WmFdReadiness> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        WmThreadAffinity.Assert();
        var source = new Source(this) { Fd = fd, Events = events, FdHandler = handler };
        _sources.Add(source);
        _dirty = true;
        return source;
    }

    public IWmEventSource AddTimer(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        WmThreadAffinity.Assert();
        var source = new Source(this) { Fd = -1, TimerHandler = handler, DeadlineMs = long.MaxValue };
        _sources.Add(source);
        return source;
    }

    public void AddIdle(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        WmThreadAffinity.Assert();
        _idle.Add(handler);
    }

    internal void Dispatch(int timeoutMs)
    {
        WmThreadAffinity.Assert();
        DrainIdle();

        var now = NowMs();
        var timeout = timeoutMs;
        foreach (var source in _sources)
        {
            if (source.IsRemoved || source.DeadlineMs == long.MaxValue)
            {
                continue;
            }

            var remaining = (int)Math.Clamp(source.DeadlineMs - now, 0, int.MaxValue);
            timeout = timeout < 0 ? remaining : Math.Min(timeout, remaining);
        }

        Rebuild();
        if (_pollBuffer.Length > 0)
        {
            int result;
            do
            {
                result = Poll(_pollBuffer, (nuint)_pollBuffer.Length, timeout);
            }
            while (result < 0 && Marshal.GetLastPInvokeError() == Eintr);
        }
        else if (timeout > 0)
        {
            Thread.Sleep(timeout);
        }

        for (var i = 0; i < _pollBuffer.Length; i++)
        {
            var revents = _pollBuffer[i].Revents;
            if (revents == 0)
            {
                continue;
            }

            var source = _pollOwners[i];
            if (source.IsRemoved || source.FdHandler is null)
            {
                continue;
            }

            source.FdHandler(source.Fd, FromPoll(revents));
        }

        now = NowMs();
        for (var i = _sources.Count - 1; i >= 0; i--)
        {
            var source = _sources[i];
            if (source.IsRemoved || source.DeadlineMs > now || source.TimerHandler is null)
            {
                continue;
            }

            source.DeadlineMs = long.MaxValue;
            source.TimerHandler();
        }

        Sweep();
    }

    internal void DrainIdle()
    {
        while (_idle.Count > 0)
        {
            _idleRunning.AddRange(_idle);
            _idle.Clear();
            foreach (var callback in _idleRunning)
            {
                callback();
            }

            _idleRunning.Clear();
        }
    }

    internal void Clear()
    {
        _sources.Clear();
        _idle.Clear();
        _dirty = true;
    }

    private void Sweep()
    {
        if (_sources.RemoveAll(static s => s.IsRemoved) > 0)
        {
            _dirty = true;
        }
    }

    private void Rebuild()
    {
        if (!_dirty)
        {
            for (var i = 0; i < _pollBuffer.Length; i++)
            {
                _pollBuffer[i].Revents = 0;
            }

            return;
        }

        var count = 0;
        foreach (var source in _sources)
        {
            if (!source.IsRemoved && source.Fd >= 0)
            {
                count++;
            }
        }

        if (_pollBuffer.Length != count)
        {
            _pollBuffer = new PollFd[count];
            _pollOwners = new Source[count];
        }

        var index = 0;
        foreach (var source in _sources)
        {
            if (source.IsRemoved || source.Fd < 0)
            {
                continue;
            }

            _pollBuffer[index] = new PollFd { Fd = source.Fd, Events = ToPoll(source.Events), Revents = 0 };
            _pollOwners[index] = source;
            index++;
        }

        _dirty = false;
    }

    private static long NowMs() => Environment.TickCount64;

    private static short ToPoll(WmFdReadiness events)
    {
        short result = 0;
        if ((events & WmFdReadiness.Readable) != 0)
        {
            result |= PollIn;
        }

        if ((events & WmFdReadiness.Writable) != 0)
        {
            result |= PollOut;
        }

        return result;
    }

    private static WmFdReadiness FromPoll(short revents)
    {
        var result = WmFdReadiness.None;
        if ((revents & PollIn) != 0)
        {
            result |= WmFdReadiness.Readable;
        }

        if ((revents & PollOut) != 0)
        {
            result |= WmFdReadiness.Writable;
        }

        if ((revents & PollHup) != 0)
        {
            result |= WmFdReadiness.Hangup;
        }

        if ((revents & PollErr) != 0 || (revents & PollNval) != 0)
        {
            result |= WmFdReadiness.Error;
        }

        return result;
    }

    private const short PollIn = 0x001;
    private const short PollOut = 0x004;
    private const short PollErr = 0x008;
    private const short PollHup = 0x010;
    private const short PollNval = 0x020;
    private const int Eintr = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int Poll([In, Out] PollFd[] fds, nuint nfds, int timeout);

    private sealed class Source(WmEventLoop loop) : IWmEventSource
    {
        public int Fd { get; init; }

        public WmFdReadiness Events { get; set; }

        public Action<int, WmFdReadiness>? FdHandler { get; init; }

        public Action? TimerHandler { get; init; }

        public long DeadlineMs { get; set; } = long.MaxValue;

        public bool IsRemoved { get; private set; }

        public void Remove()
        {
            if (IsRemoved)
            {
                return;
            }

            IsRemoved = true;
            DeadlineMs = long.MaxValue;
            loop._dirty = true;
        }

        public void UpdateTimer(int delayMs)
        {
            WmThreadAffinity.Assert();
            DeadlineMs = IsRemoved || delayMs <= 0 ? long.MaxValue : NowMs() + delayMs;
        }

        public void UpdateFd(WmFdReadiness events)
        {
            WmThreadAffinity.Assert();
            if (Events == events)
            {
                return;
            }

            Events = events;
            loop._dirty = true;
        }
    }
}
