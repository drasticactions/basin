using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class FifoManager : IDisposable, IFrameSink
{
    public const int Version = 1;

    private const int GuardDelayMs = 50;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly OutputLayout _layout;
    private readonly IEventSource _guard;
    private readonly IFrameClock? _clock;
    private readonly HashSet<Surface> _surfaces = [];
    private readonly List<IOutput> _hooked = [];
    private readonly List<Surface> _sweep = [];
    private bool _consumerLatches;
    private bool _guardArmed;
    private long _retirements;
    private long _retirementsAtGuardArm;

    public FifoManager(
        WlServerDisplay display,
        CompositorGlobal compositor,
        OutputLayout layout,
        ICompositorEventLoop loop,
        IFrameClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(loop);
        _compositor = compositor;
        _layout = layout;
        _guard = loop.AddTimer(OnGuard);
        _clock = clock;
        _global = display.CreateGlobal(WpFifoManagerV1.Interface, Version, OnBind);
        _layout.Changed += Rehook;
        Rehook();
        _clock?.Add(this);
    }

    public void BeginFrame(IOutput output, long predictedVblankNanos) => Latch(predictedVblankNanos);

    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

    public bool HasPendingBarriers
    {
        get
        {
            foreach (var surface in _surfaces)
            {
                if (surface.FifoBarrierArmed || surface.HasParkedCommits)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void LatchAtNextRefresh(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var refresh = output.CurrentMode.RefreshMilliHz;
        Latch(refresh > 0 ? MonotonicClock.Nanos + (1_000_000_000_000L / refresh) : 0);
    }

    public void Latch(long presentationTimeNanos = 0)
    {
        _consumerLatches = true;
        _retirements++;
        if (_surfaces.Count == 0)
        {
            return;
        }

        var time = presentationTimeNanos > 0 ? presentationTimeNanos : MonotonicClock.Nanos;
        _sweep.Clear();
        _sweep.AddRange(_surfaces);
        foreach (var surface in _sweep)
        {
            surface.ReleaseParkedCommits(time, refreshCycleCompleted: false);
        }

        foreach (var surface in _sweep)
        {
            surface.ClearFifoBarrier();
        }

        _sweep.Clear();
    }

    public void Dispose()
    {
        _clock?.Remove(this);
        _layout.Changed -= Rehook;
        foreach (var output in _hooked)
        {
            output.Frame -= OnFrame;
        }

        _hooked.Clear();
        _guard.Remove();
        _global.Dispose();
    }

    private void Rehook()
    {
        foreach (var output in _hooked)
        {
            output.Frame -= OnFrame;
        }

        _hooked.Clear();
        foreach (var (output, _) in _layout.Outputs)
        {
            output.Frame += OnFrame;
            _hooked.Add(output);
        }
    }

    private void OnFrame()
    {
        if (_consumerLatches)
        {
            return;
        }

        _retirements++;
        Sweep(refreshCycleCompleted: true);
    }

    private void OnGuard()
    {
        _guardArmed = false;
        if (!AnythingParked())
        {
            return;
        }

        if (_retirements == _retirementsAtGuardArm)
        {
            _retirements++;
            Sweep(refreshCycleCompleted: true);
            if (!AnythingParked())
            {
                return;
            }
        }

        ArmGuard();
    }

    private bool AnythingParked()
    {
        foreach (var surface in _surfaces)
        {
            if (surface.HasParkedCommits)
            {
                return true;
            }
        }

        return false;
    }

    private void ArmGuard()
    {
        if (!_guardArmed)
        {
            _guardArmed = true;
            _retirementsAtGuardArm = _retirements;
            _guard.UpdateTimer(GuardDelayMs);
        }
    }

    private void Sweep(bool refreshCycleCompleted)
    {
        if (_surfaces.Count == 0)
        {
            return;
        }

        var now = MonotonicClock.Nanos;
        _sweep.Clear();
        _sweep.AddRange(_surfaces);
        foreach (var surface in _sweep)
        {
            surface.ReleaseParkedCommits(now, refreshCycleCompleted);
        }

        _sweep.Clear();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpFifoManagerV1Resource(client, version, id);
        manager.GetFifo += (_, e) =>
        {
            var resource = new WpFifoV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_surfaces.Add(surface))
            {
                manager.PostError((uint)WpFifoManagerV1.Error.AlreadyExists, "surface already has a fifo object");
                return;
            }

            resource.SetBarrier += (_, _) =>
            {
                if (Alive(resource, surface))
                {
                    surface.SetFifoBarrier();
                }
            };
            resource.WaitBarrier += (_, _) =>
            {
                if (Alive(resource, surface))
                {
                    surface.WaitFifoBarrier();
                    ArmGuard();
                }
            };

            resource.Destroyed += (_, _) => _surfaces.Remove(surface);
            surface.Destroyed += () => _surfaces.Remove(surface);
        };
    }

    private static bool Alive(WpFifoV1Resource resource, Surface surface)
    {
        if (!surface.IsDestroyed)
        {
            return true;
        }

        resource.PostError((uint)WpFifoV1.Error.SurfaceDestroyed, "the fifo object's surface is gone");
        return false;
    }
}
