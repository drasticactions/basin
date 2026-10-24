using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_commit_timing_manager_v1</c> global. It holds commits that carry a target time until a frame or its timer reaches that time.
/// </summary>
public sealed class CommitTimingManager : IDisposable, IFrameSink
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly ICompositorEventLoop _loop;
    private readonly IEventSource _timer;
    private readonly IFrameClock? _clock;
    private readonly HashSet<Surface> _surfaces = [];
    private readonly List<Surface> _sweep = [];
    private bool _timerArmed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommitTimingManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces it times.</param>
    /// <param name="loop">The loop that holds the release timer.</param>
    /// <param name="clock">The frame clock to join as a sink, or <see langword="null"/> to release on the timer only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/>, <paramref name="compositor"/> or <paramref name="loop"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets or sets the clients whose target times are wall-clock time. The manager converts their times to the monotonic clock.
    /// </summary>
    public WireClockClients? WireClock { get; set; }

    /// <inheritdoc/>
    public void BeginFrame(IOutput output, long predictedVblankNanos) =>
        Release(predictedVblankNanos > 0 ? predictedVblankNanos : MonotonicClock.Nanos, predictedVblankNanos > 0 ? PresentTiming.GridOf(output) : -1);

    /// <summary>
    /// Does nothing. Commits are released at <see cref="BeginFrame"/>.
    /// </summary>
    /// <param name="output">The output that presented the frame.</param>
    /// <param name="presentedNanos">The presentation time on the monotonic clock in nanoseconds.</param>
    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

    /// <summary>
    /// Leaves the frame clock, removes the timer and removes the global.
    /// </summary>
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
                manager.PostError((uint)WpCommitTimingManagerV1.Error.CommitTimerExists, "The surface already has a commit timer.");
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
            resource.PostError((uint)WpCommitTimerV1.Error.SurfaceDestroyed, "The surface of the commit timer is destroyed.");
            return;
        }

        if (e.TvNsec >= 1_000_000_000)
        {
            resource.PostError((uint)WpCommitTimerV1.Error.InvalidTimestamp, $"tv_nsec {e.TvNsec} is not less than one second.");
            return;
        }

        if (surface.HasPendingCommitTime)
        {
            resource.PostError((uint)WpCommitTimerV1.Error.TimestampExists, "This content update already has a timestamp.");
            return;
        }

        var seconds = ((ulong)e.TvSecHi << 32) | e.TvSecLo;
        var timestamp = (long)(seconds * 1_000_000_000) + e.TvNsec;
        if (WireClock is { } wire && wire.Contains(surface.Resource.Client))
        {
            timestamp = WireClockClients.FromWire(timestamp);
        }

        surface.SetCommitTime(timestamp);
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

    private void Release(long deadlineNanos) => Release(deadlineNanos, -1);

    private void Release(long deadlineNanos, long gridNanos)
    {
        var now = deadlineNanos;
        _sweep.Clear();
        _sweep.AddRange(_surfaces);
        var nextNanos = 0L;
        foreach (var surface in _sweep)
        {
            if (surface.PacingHeld)
            {
                continue;
            }

            if (surface.ReleaseParkedCommits(now, refreshCycleCompleted: false, gridNanos))
            {
                surface.NotePacingUse();
            }
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
