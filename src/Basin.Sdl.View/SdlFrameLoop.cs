using System.Diagnostics;
using Basin.Hosted;
using static Basin.Sdl.SdlViewLog;

namespace Basin.Sdl;

/// <summary>
/// Runs the compositor host inside the SDL loop: wait, drain, tick. A present is capped at one per frame interval.
/// An idle host blocks in the SDL wait and does no work.
/// </summary>
public sealed class SdlFrameLoop : IDisposable
{
    private readonly SdlLoop _loop;
    private readonly BasinCompositorHost _host;
    private readonly Action _onFrameRequested;
    private readonly Action<SdlOutputWindow> _onDisplayChanged;
    private readonly Action<uint> _onExposedInWatch;
    private bool _ticking;
    private readonly List<SdlOutputWindow> _windows = [];
    private bool _frameWanted;
    private bool _inTick;
    private bool _frameFailing;
    private bool _wantedDuringTick;
    private long _lastFrameDone;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlFrameLoop"/> class with no windows.
    /// </summary>
    /// <param name="loop">The SDL loop to pump.</param>
    /// <param name="host">The host to tick.</param>
    public SdlFrameLoop(SdlLoop loop, BasinCompositorHost host)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(host);
        _loop = loop;
        _host = host;
        _onFrameRequested = () =>
        {
            _frameWanted = true;
            _wantedDuringTick |= _inTick;
        };
        _onDisplayChanged = _ => FollowRefresh();
        _onExposedInWatch = OnExposedInWatch;
        loop.WindowExposedInWatch += _onExposedInWatch;
        host.Scene.FrameRequested += _onFrameRequested;
    }

    /// <summary>
    /// Gets the number of ticks that presented at least one window.
    /// </summary>
    public long Frames { get; private set; }

    /// <summary>
    /// Gets the windows that the loop renders, in the order they were added.
    /// </summary>
    public IReadOnlyList<SdlOutputWindow> Windows => _windows;

    /// <summary>
    /// Adds a window to render, and sets <see cref="FrameInterval"/> from the fastest window.
    /// </summary>
    /// <param name="window">The window to add.</param>
    public void Add(SdlOutputWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _windows.Add(window);
        window.DisplayChanged += _onDisplayChanged;
        FollowRefresh();
    }

    /// <summary>
    /// Removes a window, and sets <see cref="FrameInterval"/> from the fastest window that remains.
    /// </summary>
    /// <param name="window">The window to remove.</param>
    /// <returns><see langword="true"/> when the window was in the loop.</returns>
    public bool Remove(SdlOutputWindow window)
    {
        window.DisplayChanged -= _onDisplayChanged;
        var removed = _windows.Remove(window);
        FollowRefresh();
        return removed;
    }

    private void FollowRefresh()
    {
        var fastest = 0f;
        foreach (var window in _windows)
        {
            fastest = Math.Max(fastest, window.RefreshRate);
        }

        FrameInterval = TimeSpan.FromSeconds(1.0 / (fastest > 0 ? fastest : 60.0));
    }

    private bool AnyNeedsFrame()
    {
        foreach (var window in _windows)
        {
            if (window.NeedsFrame)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the number of ticks that entered a frame.
    /// </summary>
    public long Pumps { get; private set; }

    /// <summary>
    /// Gets the shortest time between two presents and between two frame-done events.
    /// A window that is added, removed or moved to another display sets it from the fastest refresh rate, or 60 Hz.
    /// </summary>
    public TimeSpan FrameInterval { get; internal set; } = TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>
    /// Gets or sets a value indicating whether every window repaints once per frame interval, with or without damage.
    /// </summary>
    public bool Continuous { get; set; }

    private long _lastPresent;

    private int PresentDueIn()
    {
        var due = _lastPresent + (long)(FrameInterval.TotalSeconds * Stopwatch.Frequency);
        var remaining = due - Stopwatch.GetTimestamp();
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>
    /// Computes how long the next wait for SDL events can take, from pending work, the next present and the next frame callback.
    /// </summary>
    /// <returns>The wait in milliseconds, 0 to poll, or -1 to wait with no limit.</returns>
    public int Timeout()
    {
        var idle = _loop.IdleTimeout();
        if (idle == 0)
        {
            return 0;
        }

        if (_host.IsSuspended)
        {
            return idle;
        }

        if (AnyNeedsFrame())
        {
            return PresentDueIn();
        }

        if (Continuous)
        {
            var present = PresentDueIn();
            idle = idle < 0 ? present : Math.Min(idle, present);
        }

        if (_frameWanted)
        {
            var remaining = FrameDueIn();
            return idle < 0 ? remaining : Math.Min(idle, remaining);
        }

        return idle;
    }

    private int FrameDueIn()
    {
        var due = _lastFrameDone + (long)(FrameInterval.TotalSeconds * Stopwatch.Frequency);
        var remaining = due - Stopwatch.GetTimestamp();
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>
    /// Runs one iteration: waits for events, drains them, follows window visibility, and ticks the host when work is due.
    /// </summary>
    /// <param name="maxWaitMs">The longest wait in milliseconds, or -1 for no extra limit.</param>
    /// <returns><see langword="false"/> when SDL or the consumer asked to quit.</returns>
    public bool RunOnce(int maxWaitMs = -1)
    {
        var timeout = Timeout();
        if (maxWaitMs >= 0)
        {
            timeout = timeout < 0 ? maxWaitMs : Math.Min(timeout, maxWaitMs);
        }

        _loop.Pump(timeout);
        if (_loop.QuitRequested)
        {
            return false;
        }

        if (Continuous && !_host.IsSuspended && PresentDueIn() == 0)
        {
            foreach (var window in _windows)
            {
                window.View.RequestRender?.Invoke();
            }
        }

        FollowVisibility();
        var active = _loop.TakeActivity();
        var paced = _frameWanted && FrameDueIn() == 0;
        var due = AnyNeedsFrame() && PresentDueIn() == 0;
        if (active || paced || due || !_host.Wake.IsWatching)
        {
            Tick();
        }

        return !_loop.QuitRequested;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the loop suspends the host while every window is hidden, minimized or occluded.
    /// With no windows, or when this is <see langword="false"/>, the loop resumes a suspended host.
    /// </summary>
    public bool SuspendWhenHidden { get; set; } = true;

    private void FollowVisibility()
    {
        if (!SuspendWhenHidden || _windows.Count == 0)
        {
            if (_host.IsSuspended)
            {
                _host.Resume();
            }

            return;
        }

        var anyVisible = false;
        foreach (var window in _windows)
        {
            anyVisible |= !window.IsHidden;
        }

        if (!anyVisible && !_host.IsSuspended)
        {
            _host.Suspend();
        }
        else if (anyVisible && _host.IsSuspended)
        {
            _host.Resume();
        }
    }

    /// <summary>
    /// Calls <see cref="RunOnce"/> until a quit is requested.
    /// </summary>
    public void Run()
    {
        while (RunOnce())
        {
        }
    }

    private void OnExposedInWatch(uint windowId)
    {
        if (_ticking)
        {
            return;
        }

        foreach (var window in _windows)
        {
            if (window.Window.Id == windowId)
            {
                window.View.RequestRender?.Invoke();
                Tick();
                return;
            }
        }
    }

    private void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        _inTick = true;
        _wantedDuringTick = false;
        if (!_host.EnterFrame(Stopwatch.GetElapsedTime(0, now)))
        {
            _inTick = false;
            return;
        }

        _ticking = true;
        try
        {
            Pumps++;
            var presented = false;
            var attempted = false;
            var presentDue = PresentDueIn() == 0;
            for (var i = 0; i < _windows.Count; i++)
            {
                var window = _windows[i];
                if (presentDue && window.NeedsFrame)
                {
                    attempted = true;
                    if (window.Render())
                    {
                        presented = true;
                    }
                }
            }

            if (presented)
            {
                Frames++;
            }

            if (attempted)
            {
                _lastPresent = Stopwatch.GetTimestamp();
            }

            if (presented || (_frameWanted && FrameDueIn() == 0))
            {
                SendFrameDone();
            }

            _frameFailing = false;
        }
        catch (Exception error)
        {
            if (_frameFailing)
            {
                Log.Debug(error, $"frame: SDL frame failed again");
            }
            else
            {
                _frameFailing = true;
                Log.Error(error, $"frame: SDL frame failed");
            }
        }
        finally
        {
            _host.ExitFrame();
            _ticking = false;
            _inTick = false;
        }
    }

    private void SendFrameDone()
    {
        _frameWanted = _wantedDuringTick;
        _lastFrameDone = Stopwatch.GetTimestamp();
        _host.Scene.SendFrameDone((uint)Environment.TickCount);
    }

    /// <summary>
    /// Stops following the frame requests of the scene and the exposed events of the loop. It does not dispose the windows.
    /// </summary>
    public void Dispose()
    {
        _host.Scene.FrameRequested -= _onFrameRequested;
        _loop.WindowExposedInWatch -= _onExposedInWatch;
    }
}
