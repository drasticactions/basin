using System.Collections.Concurrent;
using Basin.Diagnostics;
using Basin.Hosted;
using SDL3;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

public sealed class SdlLoop : IDisposable
{
    public const int UnwatchedPollMs = 16;

    private readonly ThreadAffinity _thread;
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly List<ISdlEventSink> _sinks = [];
    private readonly Action _onWake;
    private readonly SDL.EventFilter? _modalWatch;
    private readonly int _mainThread;
    private BasinCompositorHost? _host;
    private int _wakePending;
    private bool _activity;
    private long _wakeAt = long.MaxValue;
    private bool _disposed;

    public SdlLoop(SdlLoopOptions? options = null)
    {
        options ??= new SdlLoopOptions();
        foreach (var (name, value) in DefaultHints)
        {
            SDL.SetHint(name, value);
        }

        if (options.VideoDriver is { Length: > 0 } driver)
        {
            SDL.SetHint(SDL.Hints.VideoDriver, driver);
        }

        if (options.AppName is { } appName)
        {
            SDL.SetHint(SDL.Hints.AppName, appName);
        }

        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Events))
        {
            throw new InvalidOperationException($"SDL_Init failed: {SDL.GetError()}");
        }

        _thread = ThreadAffinity.Capture();
        WakeEventType = SDL.RegisterEvents(1);
        if (WakeEventType == 0)
        {
            SDL.Quit();
            throw new InvalidOperationException($"SDL_RegisterEvents failed: {SDL.GetError()}");
        }

        VideoDriver = SDL.GetCurrentVideoDriver() ?? string.Empty;
        _onWake = Wake;
        _mainThread = Environment.CurrentManagedThreadId;
        if (OperatingSystem.IsWindows())
        {
            _modalWatch = OnWatch;
            _ = SDL.AddEventWatch(_modalWatch, 0);
        }
        Log.Info($"SDL {SDL.GetVersion()} on the {VideoDriver} video driver");
    }

    private static readonly (string Name, string Value)[] DefaultHints =
    [
        (SDL.Hints.TouchMouseEvents, "0"),
        (SDL.Hints.MouseTouchEvents, "0"),
        (SDL.Hints.PenMouseEvents, "0"),
        (SDL.Hints.PenTouchEvents, "0"),
        (SDL.Hints.QuitOnLastWindowClose, "0"),
    ];

    public uint WakeEventType { get; }

    public string VideoDriver { get; }

    public ThreadAffinity Affinity => _thread;

    public bool QuitRequested { get; private set; }

    public long Wakes { get; private set; }

    public void RequestQuit() => QuitRequested = true;

    public void Attach(BasinCompositorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _thread.Assert();
        if (_host is not null)
        {
            throw new InvalidOperationException("the loop already serves a compositor host");
        }

        _host = host;
        host.Wake.Ready += _onWake;
        host.Session.WakeupRequested += _onWake;
    }

    public void Detach()
    {
        _thread.Assert();
        if (_host is not { } host)
        {
            return;
        }

        host.Wake.Ready -= _onWake;
        host.Session.WakeupRequested -= _onWake;
        _host = null;
    }

    public void AddSink(ISdlEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _thread.Assert();
        _sinks.Add(sink);
    }

    public void RemoveSink(ISdlEventSink sink)
    {
        _thread.Assert();
        _sinks.Remove(sink);
    }

    public void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 1)
        {
            return;
        }

        var e = default(SDL.Event);
        e.Type = WakeEventType;
        if (!SDL.PushEvent(ref e))
        {
            Interlocked.Exchange(ref _wakePending, 0);
        }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _posted.Enqueue(action);
        Wake();
    }

    public event Action<uint>? WindowExposedInWatch;

    private bool OnWatch(nint userdata, ref SDL.Event e)
    {
        try
        {
            if (e.Type == (uint)SDL.EventType.WindowExposed && Environment.CurrentManagedThreadId == _mainThread)
            {
                WindowExposedInWatch?.Invoke(e.Window.WindowID);
            }
        }
        catch (Exception error)
        {
            Log.Error($"the exposed watch failed: {error}");
        }

        return true;
    }

    public void MarkActivity() => _activity = true;

    public bool TakeActivity()
    {
        var activity = _activity;
        _activity = false;
        return activity;
    }

    public void WakeAfter(int delayMs)
    {
        _thread.Assert();
        var at = Environment.TickCount64 + Math.Max(0, delayMs);
        if (at < _wakeAt)
        {
            _wakeAt = at;
        }
    }

    private int TimedWakeIn()
    {
        if (_wakeAt == long.MaxValue)
        {
            return -1;
        }

        var remaining = _wakeAt - Environment.TickCount64;
        if (remaining > 0)
        {
            return (int)Math.Min(remaining, int.MaxValue);
        }

        _wakeAt = long.MaxValue;
        _activity = true;
        return 0;
    }

    public bool HasPendingWork => _activity || !_posted.IsEmpty || Volatile.Read(ref _wakePending) == 1;

    public int IdleTimeout()
    {
        _thread.Assert();
        var timed = TimedWakeIn();
        if (HasPendingWork)
        {
            return 0;
        }

        if (_host is not { } host || host.Wake.IsWatching)
        {
            return timed;
        }

        var timer = host.Loop.NextTimerDelayMs;
        var unwatched = timer < 0 ? UnwatchedPollMs : Math.Min(timer, UnwatchedPollMs);
        return timed < 0 ? unwatched : Math.Min(unwatched, timed);
    }

    public bool Pump(int timeoutMs)
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var any = false;
        if (SDL.WaitEventTimeout(out var e, timeoutMs))
        {
            Dispatch(in e);
            any = true;
            while (SDL.PollEvent(out e))
            {
                Dispatch(in e);
            }
        }

        RunPosted();
        _ = TimedWakeIn();
        return any;
    }

    private void Dispatch(in SDL.Event e)
    {
        if (e.Type == WakeEventType)
        {
            Interlocked.Exchange(ref _wakePending, 0);
            Wakes++;
            _activity = true;
            return;
        }

        if (e.Type == (uint)SDL.EventType.Quit)
        {
            QuitRequested = true;
            _activity = true;
            return;
        }

        for (var i = 0; i < _sinks.Count; i++)
        {
            var sink = _sinks[i];
            try
            {
                if (sink.Handle(in e))
                {
                    _activity = true;
                }
            }
            catch (Exception error)
            {
                Log.Error($"an SDL event sink threw on event 0x{e.Type:x}: {error}");
            }
        }
    }

    private void RunPosted()
    {
        while (_posted.TryDequeue(out var action))
        {
            _activity = true;
            try
            {
                action();
            }
            catch (Exception error)
            {
                Log.Error($"a posted action failed on the SDL loop: {error}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        Detach();
        RunPosted();
        _disposed = true;
        _sinks.Clear();
        if (_modalWatch is not null)
        {
            SDL.RemoveEventWatch(_modalWatch, 0);
        }

        SDL.Quit();
    }
}
