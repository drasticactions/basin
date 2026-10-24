using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Host;

namespace Basin.GLib;

/// <summary>
/// Runs the default GLib main context on the compositor loop. The source acquires the context, watches its descriptors and its timeout on the loop, and dispatches its sources after each wait. Nothing calls <c>g_main_loop_run</c>. All members must be called on the thread that created the source.
/// </summary>
public sealed unsafe class GLibContextSource : IDisposable
{
    private const int MaxImmediateRounds = 4;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly ICompositorEventLoop _loop;
    private readonly BasinLogger _log;
    private readonly nint _context;
    private readonly Dictionary<int, Watch> _watches = [];
    private readonly List<int> _stale = [];
    private readonly IEventSource _timer;
    private CompositorRunLoop? _runLoop;
    private GPollFd[] _fds = new GPollFd[16];
    private int _count;
    private int _maxPriority;
    private bool _prepared;
    private bool _disposed;

    private GLibContextSource(ICompositorEventLoop loop, BasinLogger log, nint context)
    {
        _loop = loop;
        _log = log;
        _context = context;
        _timer = loop.AddTimer(OnTimer);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets a value indicating whether the source armed a loop timer for the context timeout.
    /// </summary>
    public bool TimerArmed { get; private set; }

    /// <summary>
    /// Gets the number of descriptors that the source watches for the context.
    /// </summary>
    public int WatchedDescriptors => _watches.Count;

    /// <summary>
    /// Gets the number of times that the source dispatched the context.
    /// </summary>
    public long Dispatches { get; private set; }

    /// <summary>
    /// Attaches the default GLib main context to a run loop. The source prepares the context before each wait and dispatches it after each wait.
    /// </summary>
    /// <param name="runLoop">The run loop whose iterations drive the context.</param>
    /// <param name="loop">The event loop that watches the context descriptors and its timeout.</param>
    /// <param name="log">The logger for the reason that the attach failed.</param>
    /// <returns>The source, or <see langword="null"/> when libglib does not load, the caller is not on the process main thread, or another thread owns the context.</returns>
    public static GLibContextSource? TryAttach(CompositorRunLoop runLoop, ICompositorEventLoop loop, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(runLoop);
        if (!NativeLibrary.TryLoad(GLibNative.Library, out _))
        {
            log.Info($"glib: {GLibNative.Library} did not load");
            return null;
        }

        if (GLibNative.gettid() != Environment.ProcessId)
        {
            log.Info($"glib: the loop is not on the process main thread");
            return null;
        }

        var source = TryCreate(loop, log);
        if (source is null)
        {
            return null;
        }

        source._runLoop = runLoop;
        runLoop.Iterating += source.BeforeWait;
        runLoop.Iterated += source.AfterWait;
        return source;
    }

    /// <summary>
    /// Acquires the default GLib main context for a loop that calls <see cref="BeforeWait"/> and <see cref="AfterWait"/> itself. It does not require the process main thread.
    /// </summary>
    /// <param name="loop">The event loop that watches the context descriptors and its timeout.</param>
    /// <param name="log">The logger for the reason that the attach failed.</param>
    /// <returns>The source, or <see langword="null"/> when libglib does not load or another thread owns the context.</returns>
    public static GLibContextSource? TryCreate(ICompositorEventLoop loop, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(loop);
        if (!NativeLibrary.TryLoad(GLibNative.Library, out _))
        {
            log.Info($"glib: {GLibNative.Library} did not load");
            return null;
        }

        var context = GLibNative.g_main_context_default();
        if (GLibNative.g_main_context_acquire(context) == 0)
        {
            log.Info($"glib: another thread owns the default main context");
            return null;
        }

        return new GLibContextSource(loop, log, context);
    }

    /// <summary>
    /// Prepares the context and watches what it asks for. Call it before each wait for events.
    /// </summary>
    public void BeforeWait()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        if (_prepared)
        {
            Check();
        }

        var timeout = Prepare();
        for (var round = 0; timeout == 0 && round < MaxImmediateRounds; round++)
        {
            fixed (GPollFd* fds = _fds)
            {
                _ = GLibNative.poll(fds, (nuint)_count, 0);
            }

            Check();
            timeout = Prepare();
        }

        Reconcile();
        var delay = timeout == 0 ? 1 : timeout;
        TimerArmed = delay > 0;
        _timer.UpdateTimer(delay > 0 ? delay : 0);
    }

    /// <summary>
    /// Checks the context against the descriptors that became ready and dispatches its sources. Call it after each wait for events.
    /// </summary>
    public void AfterWait()
    {
        _thread.Assert();
        if (_disposed || !_prepared)
        {
            return;
        }

        Check();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_runLoop is { } runLoop)
        {
            runLoop.Iterating -= BeforeWait;
            runLoop.Iterated -= AfterWait;
        }

        foreach (var watch in _watches.Values)
        {
            watch.Source.Remove();
        }

        _watches.Clear();
        _timer.Remove();
        GLibNative.g_main_context_release(_context);
        BasinCounters.Untrack();
    }

    private int Prepare()
    {
        int priority;
        _ = GLibNative.g_main_context_prepare(_context, &priority);
        _maxPriority = priority;
        int timeout;
        while (true)
        {
            int needed;
            fixed (GPollFd* fds = _fds)
            {
                needed = GLibNative.g_main_context_query(_context, priority, &timeout, fds, _fds.Length);
            }

            if (needed <= _fds.Length)
            {
                _count = needed;
                break;
            }

            _fds = new GPollFd[Math.Max(needed, _fds.Length * 2)];
        }

        for (var i = 0; i < _count; i++)
        {
            _fds[i].Revents = 0;
        }

        _prepared = true;
        return timeout;
    }

    private void Check()
    {
        _prepared = false;
        int ready;
        fixed (GPollFd* fds = _fds)
        {
            ready = GLibNative.g_main_context_check(_context, _maxPriority, fds, _count);
        }

        if (ready != 0)
        {
            Dispatches++;
            GLibNative.g_main_context_dispatch(_context);
        }
    }

    private void Reconcile()
    {
        foreach (var watch in _watches.Values)
        {
            watch.Wanted = FdReadiness.None;
            watch.Seen = false;
        }

        for (var i = 0; i < _count; i++)
        {
            var fd = _fds[i].Fd;
            if (fd < 0)
            {
                continue;
            }

            if (!_watches.TryGetValue(fd, out var watch))
            {
                watch = new Watch(this);
                watch.Source = _loop.AddFd(fd, FdReadiness.None, watch.Handler);
                _watches.Add(fd, watch);
            }

            watch.Seen = true;
            watch.Wanted |= ToReadiness(_fds[i].Events);
        }

        foreach (var (fd, watch) in _watches)
        {
            if (!watch.Seen)
            {
                watch.Source.Remove();
                _stale.Add(fd);
                continue;
            }

            watch.Source.UpdateFd(watch.Wanted);
            watch.Watching = watch.Wanted;
        }

        foreach (var fd in _stale)
        {
            _ = _watches.Remove(fd);
        }

        _stale.Clear();
    }

    private void OnReady(int fd, FdReadiness readiness)
    {
        if (_watches.TryGetValue(fd, out var watch) && watch.Watching != FdReadiness.None)
        {
            watch.Source.UpdateFd(FdReadiness.None);
            watch.Watching = FdReadiness.None;
        }

        if (!_prepared)
        {
            return;
        }

        var revents = FromReadiness(readiness);
        for (var i = 0; i < _count; i++)
        {
            if (_fds[i].Fd == fd)
            {
                _fds[i].Revents |= (ushort)(revents & (_fds[i].Events | GLibNative.Err | GLibNative.Hup | GLibNative.Nval));
            }
        }
    }

    private void OnTimer() => TimerArmed = false;

    private static FdReadiness ToReadiness(ushort events)
    {
        var readiness = FdReadiness.None;
        if ((events & (GLibNative.In | GLibNative.Pri)) != 0)
        {
            readiness |= FdReadiness.Readable;
        }

        if ((events & GLibNative.Out) != 0)
        {
            readiness |= FdReadiness.Writable;
        }

        return readiness;
    }

    private static ushort FromReadiness(FdReadiness readiness)
    {
        ushort events = 0;
        if ((readiness & FdReadiness.Readable) != 0)
        {
            events |= GLibNative.In | GLibNative.Pri;
        }

        if ((readiness & FdReadiness.Writable) != 0)
        {
            events |= GLibNative.Out;
        }

        if ((readiness & FdReadiness.Hangup) != 0)
        {
            events |= GLibNative.Hup;
        }

        if ((readiness & FdReadiness.Error) != 0)
        {
            events |= GLibNative.Err;
        }

        return events;
    }

    private sealed class Watch
    {
        public Watch(GLibContextSource owner) => Handler = owner.OnReady;

        public Action<int, FdReadiness> Handler { get; }

        public IEventSource Source { get; set; } = null!;

        public FdReadiness Wanted { get; set; }

        public FdReadiness Watching { get; set; }

        public bool Seen { get; set; }
    }
}
