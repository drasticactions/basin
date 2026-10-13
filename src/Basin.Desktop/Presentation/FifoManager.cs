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
    private readonly Dictionary<Surface, WpFifoV1Resource> _owners = [];
    private readonly List<IOutput> _hooked = [];
    private readonly Dictionary<IOutput, Action> _frameHandlers = [];
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

    public void BeginFrame(IOutput output, long predictedVblankNanos)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_compositor.PresentedSets.AnyRecorded)
        {
            Latch(predictedVblankNanos > 0 ? predictedVblankNanos + PresentTiming.Tolerance(output) : 0);
            return;
        }

        _consumerLatches = true;
        _retirements++;
        Sweep(output, predictedVblankNanos > 0 ? predictedVblankNanos : MonotonicClock.Nanos, latch: true,
            predictedVblankNanos > 0 ? PresentTiming.GridOf(output) : -1);
    }

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
        foreach (var surface in _surfaces)
        {
            if (!surface.PacingHeld)
            {
                _sweep.Add(surface);
            }
        }

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
        Unhook();
        _guard.Remove();
        _global.Dispose();
    }

    private void Unhook()
    {
        foreach (var output in _hooked)
        {
            if (_frameHandlers.Remove(output, out var handler))
            {
                output.Frame -= handler;
            }
        }

        _hooked.Clear();
    }

    private void Rehook()
    {
        Unhook();
        foreach (var (output, _) in _layout.Outputs)
        {
            var target = output;
            Action handler = () => OnFrame(target);
            output.Frame += handler;
            _frameHandlers[output] = handler;
            _hooked.Add(output);
        }
    }

    private void OnFrame(IOutput output)
    {
        if (_consumerLatches)
        {
            return;
        }

        _retirements++;
        if (!_compositor.PresentedSets.AnyRecorded)
        {
            Sweep(refreshCycleCompleted: true);
            return;
        }

        Sweep(output, MonotonicClock.Nanos, latch: false);
    }

    private void Sweep(IOutput output, long time, bool latch, long grid = -1)
    {
        if (_surfaces.Count == 0)
        {
            return;
        }

        _sweep.Clear();
        foreach (var surface in _surfaces)
        {
            if (ReferenceEquals(surface.MainOutput, output) && !surface.PacingHeld)
            {
                _sweep.Add(surface);
            }
        }

        foreach (var surface in _sweep)
        {
            if (surface.ReleaseParkedCommits(time, refreshCycleCompleted: !latch, grid))
            {
                surface.NotePacingUse();
            }
        }

        if (latch)
        {
            foreach (var surface in _sweep)
            {
                surface.ClearFifoBarrier();
            }
        }

        _sweep.Clear();
    }

    private void SweepUnowned()
    {
        var now = MonotonicClock.Nanos;
        _sweep.Clear();
        foreach (var surface in _surfaces)
        {
            if (surface.MainOutput is null)
            {
                _sweep.Add(surface);
            }
        }

        foreach (var surface in _sweep)
        {
            surface.ReleaseParkedCommits(now, refreshCycleCompleted: true);
        }

        _sweep.Clear();
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
        else if (_compositor.PresentedSets.AnyRecorded)
        {
            SweepUnowned();
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
        foreach (var output in _hooked)
        {
            output.RequestRepaint();
        }

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

            if (_owners.ContainsKey(surface))
            {
                manager.PostError((uint)WpFifoManagerV1.Error.AlreadyExists, "surface already has a fifo object");
                return;
            }

            _owners[surface] = resource;
            _surfaces.Add(surface);

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

            resource.Destroyed += (_, _) =>
            {
                if (_owners.TryGetValue(surface, out var owner) && ReferenceEquals(owner, resource))
                {
                    _owners.Remove(surface);
                }
            };
            surface.Destroyed += () =>
            {
                _owners.Remove(surface);
                _surfaces.Remove(surface);
            };
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
