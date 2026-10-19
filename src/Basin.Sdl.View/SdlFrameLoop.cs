using System.Diagnostics;
using Basin.Hosted;
using static Basin.Sdl.SdlViewLog;

namespace Basin.Sdl;

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
    private bool _wantedDuringTick;
    private long _lastFrameDone;

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

    public long Frames { get; private set; }

    public IReadOnlyList<SdlOutputWindow> Windows => _windows;

    public void Add(SdlOutputWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _windows.Add(window);
        window.DisplayChanged += _onDisplayChanged;
        FollowRefresh();
    }

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

    public long Pumps { get; private set; }

    public TimeSpan FrameInterval { get; set; } = TimeSpan.FromSeconds(1.0 / 60.0);

    public bool Continuous { get; set; }

    private long _lastPresent;

    private int PresentDueIn()
    {
        var due = _lastPresent + (long)(FrameInterval.TotalSeconds * Stopwatch.Frequency);
        var remaining = due - Stopwatch.GetTimestamp();
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency);
    }

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
            var presentDue = PresentDueIn() == 0;
            for (var i = 0; i < _windows.Count; i++)
            {
                var window = _windows[i];
                if (presentDue && window.NeedsFrame && window.Render())
                {
                    presented = true;
                }
            }

            if (presented)
            {
                Frames++;
                _lastPresent = Stopwatch.GetTimestamp();
            }

            if (presented || (_frameWanted && FrameDueIn() == 0))
            {
                SendFrameDone();
            }
        }
        catch (Exception error)
        {
            Log.Error($"the SDL frame failed: {error}");
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

    public void Dispose()
    {
        _host.Scene.FrameRequested -= _onFrameRequested;
        _loop.WindowExposedInWatch -= _onExposedInWatch;
    }
}
