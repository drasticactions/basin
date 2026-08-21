using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class CommitTimingManager : IDisposable, IFrameSink
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly ICompositorEventLoop _loop;
    private readonly IEventSource _timer;
    private readonly IFrameClock? _clock;
    private readonly HashSet<Surface> _surfaces = [];
    private readonly List<Surface> _sweep = [];
    private bool _timerArmed;

    public CommitTimingManager(
        WlServerDisplay display,
        CompositorGlobal compositor,
        ICompositorEventLoop loop,
        IFrameClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(loop);
        _compositor = compositor;
        _loop = loop;
        _timer = loop.AddTimer(OnTimer);
        _clock = clock;
        _global = display.CreateGlobal(WpCommitTimingManagerV1.Interface, Version, OnBind);
        _clock?.Add(this);
    }

    public void BeginFrame(IOutput output, long predictedVblankNanos) =>
        Release(predictedVblankNanos > 0 ? predictedVblankNanos : MonotonicClock.Nanos);

    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

    public void Dispose()
    {
        _clock?.Remove(this);
        _timer.Remove();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpCommitTimingManagerV1Resource(client, version, id);
        manager.GetTimer += (_, e) =>
        {
            var resource = new WpCommitTimerV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_surfaces.Add(surface))
            {
                manager.PostError((uint)WpCommitTimingManagerV1.Error.CommitTimerExists, "surface already has a commit timer");
                return;
            }

            resource.SetTimestamp += (_, te) => OnSetTimestamp(resource, surface, te);
            resource.Destroyed += (_, _) => _surfaces.Remove(surface);
            surface.Destroyed += () => _surfaces.Remove(surface);
        };
    }

    private void OnSetTimestamp(WpCommitTimerV1Resource resource, Surface surface, WpCommitTimerV1Resource.SetTimestampEventArgs e)
    {
        if (surface.IsDestroyed)
        {
            resource.PostError((uint)WpCommitTimerV1.Error.SurfaceDestroyed, "the commit timer's surface is gone");
            return;
        }

        if (e.TvNsec >= 1_000_000_000)
        {
            resource.PostError((uint)WpCommitTimerV1.Error.InvalidTimestamp, $"tv_nsec {e.TvNsec} is not below one second");
            return;
        }

        if (surface.HasPendingCommitTime)
        {
            resource.PostError((uint)WpCommitTimerV1.Error.TimestampExists, "this content update already carries a timestamp");
            return;
        }

        var seconds = ((ulong)e.TvSecHi << 32) | e.TvSecLo;
        surface.SetCommitTime((long)(seconds * 1_000_000_000) + e.TvNsec);
        ArmTimer();
    }

    private void ArmTimer()
    {
        if (_timerArmed)
        {
            return;
        }

        _timerArmed = true;
        _loop.AddIdle(() =>
        {
            _timerArmed = false;
            Release(MonotonicClock.Nanos);
        });
    }

    private void OnTimer()
    {
        _timerArmed = false;
        Release(MonotonicClock.Nanos);
    }

    private void Release(long deadlineNanos)
    {
        var now = deadlineNanos;
        _sweep.Clear();
        _sweep.AddRange(_surfaces);
        var nextNanos = 0L;
        foreach (var surface in _sweep)
        {
            surface.ReleaseParkedCommits(now, refreshCycleCompleted: false);
            var target = surface.NextParkedCommitTimeNanos;
            if (target > 0 && (nextNanos == 0 || target < nextNanos))
            {
                nextNanos = target;
            }
        }

        _sweep.Clear();

        var delayNanos = nextNanos - MonotonicClock.Nanos;
        if (nextNanos > 0 && delayNanos > 0)
        {
            _timerArmed = true;
            _timer.UpdateTimer((int)Math.Max(1, delayNanos / 1_000_000));
        }
    }
}
