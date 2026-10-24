using System.Collections.Concurrent;
using Basin.Diagnostics;
using Basin.Hosted;
using SDL3;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

/// <summary>
/// Owns SDL initialization and the event pump of an SDL host. The thread that constructs it is the SDL main thread and the compositor thread.
/// Only <see cref="Wake"/> and <see cref="Post"/> are safe from another thread.
/// </summary>
public sealed class SdlLoop : IDisposable
{
    /// <summary>
    /// The longest idle wait in milliseconds when the host has no loop descriptor to watch.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlLoop"/> class. It sets the input hints, initializes SDL video and events, and captures the thread.
    /// The consumer must construct the compositor host after the loop, on the same thread.
    /// </summary>
    /// <param name="options">The application name and the video driver, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="InvalidOperationException">SDL cannot initialize or register the wake event.</exception>
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
            throw new InvalidOperationException($"SDL_Init failed with \"{SDL.GetError()}\".");
        }

        _thread = ThreadAffinity.Capture();
        WakeEventType = SDL.RegisterEvents(1);
        if (WakeEventType == 0)
        {
            SDL.Quit();
            throw new InvalidOperationException($"SDL_RegisterEvents failed with \"{SDL.GetError()}\".");
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

    /// <summary>
    /// Gets the SDL user event type that <see cref="Wake"/> pushes. The loop consumes these events and no sink sees them.
    /// </summary>
    public uint WakeEventType { get; }

    /// <summary>
    /// Gets the name of the SDL video driver in use, for example "wayland", "x11" or "offscreen".
    /// </summary>
    public string VideoDriver { get; }

    /// <summary>
    /// Gets the affinity of the SDL main thread.
    /// </summary>
    public ThreadAffinity Affinity => _thread;

    /// <summary>
    /// Gets a value indicating whether SDL sent a quit event or the consumer called <see cref="RequestQuit"/>.
    /// </summary>
    public bool QuitRequested { get; private set; }

    /// <summary>
    /// Gets the number of wake events that the loop consumed. Coalesced wakes count once.
    /// </summary>
    public long Wakes { get; private set; }

    /// <summary>
    /// Sets <see cref="QuitRequested"/>. It does not wake the loop.
    /// </summary>
    public void RequestQuit() => QuitRequested = true;

    /// <summary>
    /// Wakes the loop whenever the host has work: when its loop descriptor is ready or its session asks for a wakeup.
    /// </summary>
    /// <param name="host">The host to serve.</param>
    /// <exception cref="InvalidOperationException">The loop already serves a host.</exception>
    public void Attach(BasinCompositorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _thread.Assert();
        if (_host is not null)
        {
            throw new InvalidOperationException("The loop already serves a compositor host.");
        }

        _host = host;
        host.Wake.Ready += _onWake;
        host.Session.WakeupRequested += _onWake;
    }

    /// <summary>
    /// Stops the wakes from the attached host. Without a host it does nothing.
    /// </summary>
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

    /// <summary>
    /// Adds a sink that receives every SDL event the loop drains, after the sinks that were added before it.
    /// </summary>
    /// <param name="sink">The sink to add.</param>
    public void AddSink(ISdlEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _thread.Assert();
        _sinks.Add(sink);
    }

    /// <summary>
    /// Removes a sink. A sink that is not present is ignored.
    /// </summary>
    /// <param name="sink">The sink to remove.</param>
    public void RemoveSink(ISdlEventSink sink)
    {
        _thread.Assert();
        _sinks.Remove(sink);
    }

    /// <summary>
    /// Pushes one wake event, so that a blocked <see cref="Pump"/> returns. It is safe from any thread.
    /// A wake that is still pending makes the call do nothing.
    /// </summary>
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

    /// <summary>
    /// Queues an action to run on the SDL main thread and wakes the loop. It is safe from any thread.
    /// The loop runs the queue after it drains the SDL events, and logs an exception that an action raises.
    /// </summary>
    /// <param name="action">The action to run.</param>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _posted.Enqueue(action);
        Wake();
    }

    /// <summary>
    /// Occurs on Windows only, when an exposed event reaches the event watch on the main thread.
    /// This lets a window repaint while the Win32 modal loop of a move or resize blocks <see cref="Pump"/>. The argument is the window id.
    /// </summary>
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
            Log.Error(error, $"events: exposed watch failed");
        }

        return true;
    }

    /// <summary>
    /// Records that work is pending, so that <see cref="IdleTimeout"/> returns 0 until <see cref="TakeActivity"/> clears it.
    /// </summary>
    public void MarkActivity() => _activity = true;

    /// <summary>
    /// Reads and clears the activity flag. A wake, a quit, a posted action and an event that a sink took set the flag.
    /// </summary>
    /// <returns><see langword="true"/> when there was activity since the last call.</returns>
    public bool TakeActivity()
    {
        var activity = _activity;
        _activity = false;
        return activity;
    }

    /// <summary>
    /// Asks the loop to end its idle wait after a delay, and to mark activity then. An earlier request wins.
    /// </summary>
    /// <param name="delayMs">The delay in milliseconds. A negative value counts as 0.</param>
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

    /// <summary>
    /// Gets a value indicating whether activity, a posted action or a wake is pending.
    /// </summary>
    public bool HasPendingWork => _activity || !_posted.IsEmpty || Volatile.Read(ref _wakePending) == 1;

    /// <summary>
    /// Computes how long the next <see cref="Pump"/> can wait.
    /// </summary>
    /// <returns>0 when work is pending, -1 to wait with no limit, else the wait in milliseconds. It is at most <see cref="UnwatchedPollMs"/> when the host has no descriptor to watch.</returns>
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

    /// <summary>
    /// Waits for an SDL event, drains every pending event to the sinks, and then runs the posted actions.
    /// </summary>
    /// <param name="timeoutMs">The longest wait in milliseconds, 0 to poll, or -1 to wait with no limit.</param>
    /// <returns><see langword="true"/> when at least one SDL event arrived.</returns>
    /// <exception cref="ObjectDisposedException">The loop is disposed.</exception>
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
                Log.Error(error, $"events: sink failed on event 0x{e.Type:x}");
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
                Log.Error(error, $"loop: posted action failed");
            }
        }
    }

    /// <summary>
    /// Detaches the host, runs the posted actions that remain, drops the sinks and quits SDL. A second call does nothing.
    /// </summary>
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
