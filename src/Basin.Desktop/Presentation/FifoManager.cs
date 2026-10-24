using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_fifo_manager_v1</c> global. It holds fifo commits until the output latches a frame. A guard timer releases them when no frame comes.
/// </summary>
public sealed class FifoManager : IDisposable, IFrameSink, IWpFifoManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="FifoManager"/> class and creates the global. Until a consumer latches, the output frame events latch.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces it paces.</param>
    /// <param name="layout">The layout whose outputs it follows.</param>
    /// <param name="loop">The loop that holds the guard timer.</param>
    /// <param name="clock">The frame clock to join as a sink, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/>, <paramref name="compositor"/>, <paramref name="layout"/> or <paramref name="loop"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Latches the fifo surfaces of the frame. After this call, output frame events no longer latch.
    /// </summary>
    /// <param name="output">The output that the frame is for.</param>
    /// <param name="predictedVblankNanos">The predicted vblank on the monotonic clock in nanoseconds, or 0 to latch now.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Does nothing. Fifo barriers clear at <see cref="BeginFrame"/>.
    /// </summary>
    /// <param name="output">The output that presented the frame.</param>
    /// <param name="presentedNanos">The presentation time on the monotonic clock in nanoseconds.</param>
    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

    /// <summary>
    /// Gets a value indicating whether a fifo surface has an armed barrier or a held commit.
    /// </summary>
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

    /// <summary>
    /// Latches every fifo surface with a presentation time one refresh from now.
    /// </summary>
    /// <param name="output">The output whose refresh rate sets the time.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void LatchAtNextRefresh(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var refresh = output.CurrentMode.RefreshMilliHz;
        Latch(refresh > 0 ? MonotonicClock.Nanos + (1_000_000_000_000L / refresh) : 0);
    }

    /// <summary>
    /// Releases the held commits and clears the barriers of every fifo surface that the frame limiter does not hold. After this call, output frame events no longer latch.
    /// </summary>
    /// <param name="presentationTimeNanos">The presentation time on the monotonic clock in nanoseconds, or 0 for now.</param>
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

    /// <summary>
    /// Leaves the frame clock, stops following the outputs, removes the guard timer and removes the global.
    /// </summary>
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

    void IWpFifoManagerV1Handler.Destroy(WpFifoManagerV1Resource resource)
    {
    }

    void IWpFifoManagerV1Handler.GetFifo(WpFifoManagerV1Resource resource, in WpFifoManagerV1Resource.GetFifoEventArgs args)
    {
        var fifo = new Fifo(this);
        var fifoResource = new WpFifoV1Resource(resource.Client, resource.Version, args.Id, fifo);
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            return;
        }

        if (_owners.ContainsKey(surface))
        {
            resource.PostError((uint)WpFifoManagerV1.Error.AlreadyExists, "The surface already has a fifo object.");
            return;
        }

        _owners[surface] = fifoResource;
        _surfaces.Add(surface);
        fifo.Surface = surface;

        fifoResource.Destroyed += (_, _) =>
        {
            if (_owners.TryGetValue(surface, out var owner) && ReferenceEquals(owner, fifoResource))
            {
                _owners.Remove(surface);
            }
        };
        surface.Destroyed += () =>
        {
            _owners.Remove(surface);
            _surfaces.Remove(surface);
        };
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new WpFifoManagerV1Resource(client, version, id, this);

    private static bool Alive(WpFifoV1Resource resource, Surface surface)
    {
        if (!surface.IsDestroyed)
        {
            return true;
        }

        resource.PostError((uint)WpFifoV1.Error.SurfaceDestroyed, "The surface of the fifo object is destroyed.");
        return false;
    }

    private sealed class Fifo(FifoManager owner) : IWpFifoV1Handler
    {
        public Surface? Surface { get; set; }

        void IWpFifoV1Handler.SetBarrier(WpFifoV1Resource resource)
        {
            if (Surface is { } surface && Alive(resource, surface))
            {
                surface.SetFifoBarrier();
            }
        }

        void IWpFifoV1Handler.WaitBarrier(WpFifoV1Resource resource)
        {
            if (Surface is { } surface && Alive(resource, surface))
            {
                surface.WaitFifoBarrier();
                owner.ArmGuard();
            }
        }

        void IWpFifoV1Handler.Destroy(WpFifoV1Resource resource)
        {
        }
    }
}
