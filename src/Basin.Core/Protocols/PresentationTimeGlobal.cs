using System.Diagnostics;
using Basin.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class PresentationTimeGlobal : IDisposable
{
    public const int Version = 2;
    public const uint ClockMonotonic = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, SurfaceFeedback> _surfaces = [];

    public PresentationTimeGlobal(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpPresentation.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

    public void Sampled(Surface surface)
    {
        if (_surfaces.TryGetValue(surface, out var feedback))
        {
            feedback.Latch();
        }
    }

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

        var shown = feedback.TakeShown();
        shown?.SendPresented(output, timeNanoseconds, refreshNanoseconds, sequence, flags);
    }

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
        foreach (var surface in _surfaces.Keys)
        {
            Presented(surface, output, timeNanoseconds, refreshNanoseconds, sequence, flags);
        }
    }

    internal void PresentAllNowCore(IOutput output)
    {
        var timeNanoseconds = (ulong)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));
        PresentAllCore(
            output, timeNanoseconds, output.CurrentMode.RefreshIntervalNanoseconds, 0, PresentedFlags.Vsync);
    }

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

    public void Discarded(Surface surface)
    {
        if (_surfaces.TryGetValue(surface, out var feedback))
        {
            feedback.DiscardAll();
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var presentation = new WpPresentationResource(client, version, id);
        presentation.SendClockId(ClockMonotonic);

        presentation.Feedback += (_, e) =>
        {
            var resource = new WpPresentationFeedbackResource(client, presentation.Version, e.Callback);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                resource.SendDiscarded();
                resource.Destroy();
                return;
            }

            var feedback = TrackSurface(surface);
            feedback.Pending.Add(resource);
            resource.Destroyed += (_, _) =>
            {
                feedback.Pending.Remove(resource);
                feedback.Forget(resource);
                surface.Pending.GetExtension<PendingFeedback>()?.Forget(resource);
                surface.Current.GetExtension<PendingFeedback>()?.Forget(resource);
            };
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
        };
        surface.Destroyed += () =>
        {
            feedback.DiscardAll();
            _surfaces.Remove(surface);
        };
        return feedback;
    }

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
                    refreshNanoseconds,
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

        public void Enqueue(PendingFeedback carrier) => _committed.Add(carrier);

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
