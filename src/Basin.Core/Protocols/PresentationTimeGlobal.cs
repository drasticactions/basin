using Basin.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// Serves <c>wp_presentation</c> and sends feedback for the content that each surface had when an output presented a frame.
/// </summary>
public sealed class PresentationTimeGlobal : IDisposable, IWpPresentationHandler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 2;
    /// <summary>
    /// The clock ID of <c>CLOCK_MONOTONIC</c>.
    /// </summary>
    public const uint ClockMonotonic = 1;
    /// <summary>
    /// The clock ID of <c>CLOCK_REALTIME</c>.
    /// </summary>
    public const uint ClockRealtime = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, SurfaceFeedback> _surfaces = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationTimeGlobal"/> class.
    /// </summary>
    /// <param name="display">The display that the global belongs to.</param>
    /// <param name="compositor">The compositor global that resolves surfaces and records presented sets.</param>
    public PresentationTimeGlobal(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpPresentation.Interface, Version, OnBind);
    }

    /// <summary>
    /// Destroys the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Gets or sets the clock that the global announces to clients that bind after the change. The default is <see cref="ClockMonotonic"/>.
    /// </summary>
    public uint ClockId { get; set; } = ClockMonotonic;

    /// <summary>
    /// Gets or sets the clients that receive wall-clock times instead of monotonic times, or <see langword="null"/> for none.
    /// </summary>
    public WireClockClients? WireClock { get; set; }

    /// <summary>
    /// Gets the current time of the clock that <see cref="ClockId"/> names, in nanoseconds.
    /// </summary>
    public ulong NowNanoseconds =>
        (ulong)(ClockId == ClockRealtime ? RealtimeClock.Nanos : MonotonicClock.Nanos);

    /// <summary>
    /// Latches the feedback of a surface: the content that it has now is the content that the next presentation shows.
    /// </summary>
    /// <param name="surface">The surface whose content was sampled.</param>
    public void Sampled(Surface surface)
    {
        if (_surfaces.TryGetValue(surface, out var feedback))
        {
            feedback.Latch();
        }
    }

    /// <summary>
    /// Latches the feedback of every surface. After the first call, the automatic sampling on output commits stops, and the consumer owns sampling.
    /// </summary>
    public void SampleAll()
    {
        ConsumerSamples = true;
        SampleAllCore();
    }

    internal bool ConsumerSamples { get; private set; }

    internal void SampleAllCore()
    {
        foreach (var feedback in _surfaces.Values)
        {
            feedback.Latch();
        }
    }

    /// <summary>
    /// Sends <c>presented</c> for the latched feedback of one surface. The refresh is raised to the pacing interval of the surface when that is longer.
    /// </summary>
    /// <param name="surface">The surface that was presented.</param>
    /// <param name="output">The output that showed the surface, or <see langword="null"/>.</param>
    /// <param name="timeNanoseconds">The presentation time on the clock of the global.</param>
    /// <param name="refreshNanoseconds">The refresh period of the output, or 0 when it is unknown.</param>
    /// <param name="sequence">The vertical retrace counter, or 0 when it is unknown.</param>
    /// <param name="flags">The presentation flags.</param>
    public void Presented(
        Surface surface,
        IOutput? output,
        ulong timeNanoseconds,
        uint refreshNanoseconds,
        ulong sequence,
        PresentedFlags flags)
    {
        if (!_surfaces.TryGetValue(surface, out var feedback))
        {
            return;
        }

        if (surface.PacingIntervalNanos > refreshNanoseconds)
        {
            refreshNanoseconds = surface.PacingIntervalNanos;
        }

        var shown = feedback.TakeShown();
        if (WireClock is { } wire && wire.Contains(surface.Resource.Client))
        {
            timeNanoseconds = WireClockClients.ToWire(timeNanoseconds);
        }

        shown?.SendPresented(output, timeNanoseconds, refreshNanoseconds, sequence, flags);
    }

    /// <summary>
    /// Sends <c>presented</c> for the latched feedback of every surface. After the first call, the automatic presentation from output events stops, and the consumer owns presentation.
    /// </summary>
    /// <param name="output">The output that showed the frame, or <see langword="null"/>.</param>
    /// <param name="timeNanoseconds">The presentation time on the clock of the global.</param>
    /// <param name="refreshNanoseconds">The refresh period of the output, or 0 when it is unknown.</param>
    /// <param name="sequence">The vertical retrace counter, or 0 when it is unknown.</param>
    /// <param name="flags">The presentation flags.</param>
    public void PresentAll(
        IOutput? output,
        ulong timeNanoseconds,
        uint refreshNanoseconds,
        ulong sequence,
        PresentedFlags flags)
    {
        ConsumerPresents = true;
        PresentAllCore(output, timeNanoseconds, refreshNanoseconds, sequence, flags);
    }

    /// <summary>
    /// Sends <c>presented</c> for every surface with the current time and the refresh of the output mode. Like <see cref="PresentAll"/>, it stops the automatic presentation.
    /// </summary>
    /// <param name="output">The output that showed the frame.</param>
    public void PresentAllNow(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ConsumerPresents = true;
        PresentAllNowCore(output);
    }

    internal bool ConsumerPresents { get; private set; }

    internal void PresentAllCore(
        IOutput? output,
        ulong timeNanoseconds,
        uint refreshNanoseconds,
        ulong sequence,
        PresentedFlags flags)
    {
        var grid = output is null ? 0 : PresentTiming.GridOf(output);
        foreach (var surface in _surfaces.Keys)
        {
            surface.NotePresented((long)timeNanoseconds, grid);
            Presented(surface, output, timeNanoseconds, refreshNanoseconds, sequence, flags);
        }
    }

    internal void PresentAllNowCore(IOutput output)
    {
        var timeNanoseconds = NowNanoseconds;
        PresentAllCore(
            output, timeNanoseconds, output.CurrentMode.RefreshIntervalNanoseconds, 0, PresentedFlags.Vsync);
    }

    internal PresentedSet? SetFor(IOutput output) => _compositor.PresentedSets.Find(output);

    internal void SampleOutputCore(IOutput output)
    {
        if (SetFor(output) is not { } set)
        {
            foreach (var (surface, unowned) in _surfaces)
            {
                if (surface.MainOutput is null)
                {
                    unowned.Latch();
                }
            }

            return;
        }

        for (var i = 0; i < set.Count; i++)
        {
            var surface = set[i];
            if (set.IsMain(surface) && _surfaces.TryGetValue(surface, out var feedback))
            {
                feedback.Latch();
            }
        }
    }

    internal void PresentOutputCore(
        IOutput output,
        ulong timeNanoseconds,
        uint refreshNanoseconds,
        ulong sequence,
        PresentedFlags flags)
    {
        if (SetFor(output) is not { } set)
        {
            if (!_compositor.PresentedSets.AnyRecorded)
            {
                PresentAllCore(output, timeNanoseconds, refreshNanoseconds, sequence, flags);
                return;
            }

            foreach (var surface in _surfaces.Keys)
            {
                if (surface.MainOutput is null)
                {
                    Presented(surface, output, timeNanoseconds, refreshNanoseconds, sequence, flags);
                }
            }

            return;
        }

        var grid = PresentTiming.GridOf(output);
        for (var i = 0; i < set.Count; i++)
        {
            var surface = set[i];
            if (set.IsMain(surface))
            {
                surface.NotePresented((long)timeNanoseconds, grid);
                Presented(surface, output, timeNanoseconds, refreshNanoseconds, sequence, flags);
            }
        }
    }

    internal void PresentOutputNowCore(IOutput output) =>
        PresentOutputCore(
            output, NowNanoseconds, output.CurrentMode.RefreshIntervalNanoseconds, 0, PresentedFlags.Vsync);

    internal void DiscardOutputCore(IOutput output)
    {
        if (SetFor(output) is not { } set)
        {
            foreach (var (surface, unowned) in _surfaces)
            {
                if (surface.MainOutput is null)
                {
                    unowned.DiscardAll();
                }
            }

            return;
        }

        for (var i = 0; i < set.Count; i++)
        {
            var surface = set[i];
            if (set.IsMain(surface))
            {
                Discarded(surface);
            }
        }
    }

    /// <summary>
    /// Sends <c>discarded</c> for the feedback of every surface. Like <see cref="PresentAll"/>, it stops the automatic presentation.
    /// </summary>
    public void DiscardAll()
    {
        ConsumerPresents = true;
        DiscardAllCore();
    }

    internal void DiscardAllCore()
    {
        foreach (var surface in _surfaces.Keys)
        {
            Discarded(surface);
        }
    }

    /// <summary>
    /// Sends <c>discarded</c> for every pending feedback of a surface.
    /// </summary>
    /// <param name="surface">The surface whose content was not shown.</param>
    public void Discarded(Surface surface)
    {
        if (_surfaces.TryGetValue(surface, out var feedback))
        {
            feedback.DiscardAll();
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var presentation = new WpPresentationResource(client, version, id, this);
        presentation.SendClockId(ClockId);
    }

    void IWpPresentationHandler.Destroy(WpPresentationResource resource)
    {
    }

    void IWpPresentationHandler.Feedback(WpPresentationResource resource, in WpPresentationResource.FeedbackEventArgs args)
    {
        var feedbackResource = new WpPresentationFeedbackResource(resource.Client, resource.Version, args.Callback);
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            feedbackResource.SendDiscarded();
            feedbackResource.Destroy();
            return;
        }

        var feedback = TrackSurface(surface);
        feedback.Pending.Add(feedbackResource);
        WatchFeedback(feedbackResource, feedback, surface);
    }

    private static void WatchFeedback(WpPresentationFeedbackResource resource, SurfaceFeedback feedback, Surface surface)
    {
        resource.Destroyed += (_, _) =>
        {
            feedback.Pending.Remove(resource);
            feedback.Forget(resource);
            surface.Pending.GetExtension<PendingFeedback>()?.Forget(resource);
            surface.Current.GetExtension<PendingFeedback>()?.Forget(resource);
        };
    }

    private SurfaceFeedback TrackSurface(Surface surface)
    {
        if (_surfaces.TryGetValue(surface, out var existing))
        {
            return existing;
        }

        var feedback = new SurfaceFeedback();
        _surfaces[surface] = feedback;

        surface.CommitRequested += () =>
        {
            if (feedback.Pending.Count > 0)
            {
                surface.Pending.SetExtension(feedback.Rent());
            }
        };
        surface.Committed += () =>
        {
            if (surface.Current.TakeExtension<PendingFeedback>() is { } carried)
            {
                feedback.Enqueue(carried);
            }
            else
            {
                feedback.DiscardCommitted();
            }
        };
        surface.Destroyed += () =>
        {
            feedback.DiscardAll();
            _surfaces.Remove(surface);
        };
        return feedback;
    }

    internal static bool HasPendingFeedback(SurfaceState state) => state.GetExtension<PendingFeedback>() is not null;

    private sealed class PendingFeedback : IDisposable
    {
        private readonly SurfaceFeedback _owner;
        private readonly List<WpPresentationFeedbackResource> _resources = [];

        public PendingFeedback(SurfaceFeedback owner) => _owner = owner;

        public void Fill(List<WpPresentationFeedbackResource> resources)
        {
            _resources.AddRange(resources);
            resources.Clear();
        }

        public void Forget(WpPresentationFeedbackResource resource) => _resources.Remove(resource);

        public void SendPresented(
            IOutput? output,
            ulong timeNanoseconds,
            uint refreshNanoseconds,
            ulong sequence,
            PresentedFlags flags)
        {
            var seconds = timeNanoseconds / 1_000_000_000;
            var nanoseconds = (uint)(timeNanoseconds % 1_000_000_000);
            var outputs = output is null ? null : OutputGlobal.For(output)?.Resources;
            var variable = output is { AdaptiveSync: true };
            if (output is { LastFlipWasAsync: true })
            {
                flags &= ~PresentedFlags.Vsync;
            }

            while (_resources.Count > 0)
            {
                var resource = _resources[0];
                _resources.RemoveAt(0);
                if (resource.IsDestroyed)
                {
                    continue;
                }

                if (outputs is not null)
                {
                    for (var i = 0; i < outputs.Count; i++)
                    {
                        var bound = outputs[i];
                        if (!bound.IsDestroyed && bound.Client == resource.Client)
                        {
                            resource.SendSyncOutput(bound);
                        }
                    }
                }

                resource.SendPresented(
                    (uint)(seconds >> 32),
                    (uint)seconds,
                    nanoseconds,
                    variable && resource.Version < 2 ? 0u : refreshNanoseconds,
                    (uint)(sequence >> 32),
                    (uint)sequence,
                    (WpPresentationFeedback.Kind)flags);
                resource.Destroy();
            }

            _owner.Recycle(this);
        }

        public void Dispose()
        {
            while (_resources.Count > 0)
            {
                var resource = _resources[0];
                _resources.RemoveAt(0);
                if (!resource.IsDestroyed)
                {
                    resource.SendDiscarded();
                    resource.Destroy();
                }
            }

            _owner.Recycle(this);
        }
    }

    private sealed class SurfaceFeedback
    {
        private readonly Stack<PendingFeedback> _free = new();
        private readonly List<PendingFeedback> _committed = [];
        private PendingFeedback? _shown;

        public List<WpPresentationFeedbackResource> Pending { get; } = [];

        public PendingFeedback Rent()
        {
            var carrier = _free.Count > 0 ? _free.Pop() : new PendingFeedback(this);
            carrier.Fill(Pending);
            return carrier;
        }

        public void Recycle(PendingFeedback carrier) => _free.Push(carrier);

        public void Enqueue(PendingFeedback carrier)
        {
            foreach (var replaced in _committed)
            {
                replaced.Dispose();
            }

            _committed.Clear();
            _committed.Add(carrier);
        }

        public void Latch()
        {
            if (_committed.Count == 0)
            {
                return;
            }

            _shown?.Dispose();
            for (var i = 0; i < _committed.Count - 1; i++)
            {
                _committed[i].Dispose();
            }

            _shown = _committed[^1];
            _committed.Clear();
        }

        public PendingFeedback? TakeShown()
        {
            if (_shown is null)
            {
                Latch();
            }

            var shown = _shown;
            _shown = null;
            return shown;
        }

        public void DiscardCommitted()
        {
            if (_committed.Count == 0)
            {
                return;
            }

            foreach (var carrier in _committed)
            {
                carrier.Dispose();
            }

            _committed.Clear();
        }

        public void DiscardAll()
        {
            _shown?.Dispose();
            _shown = null;
            foreach (var carrier in _committed)
            {
                carrier.Dispose();
            }

            _committed.Clear();
        }

        public void Forget(WpPresentationFeedbackResource resource)
        {
            _shown?.Forget(resource);
            foreach (var carrier in _committed)
            {
                carrier.Forget(resource);
            }
        }
    }
}
