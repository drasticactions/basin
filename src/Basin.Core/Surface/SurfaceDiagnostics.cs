using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// Streams commit and frame-callback traces for the surfaces of one <see cref="CompositorGlobal"/>. It hooks surfaces only while at least one watcher exists.
/// </summary>
public sealed class SurfaceDiagnostics : ISurfaceDiagnostics
{
    private readonly CompositorGlobal _compositor;
    private readonly List<Watcher> _watchers = [];
    private readonly Dictionary<Surface, Hooks> _hooked = [];

    /// <summary>Initializes a new instance of the <see cref="SurfaceDiagnostics"/> class.</summary>
    /// <param name="compositor">The compositor whose surfaces are traced.</param>
    /// <exception cref="ArgumentNullException"><paramref name="compositor"/> is <see langword="null"/>.</exception>
    public SurfaceDiagnostics(CompositorGlobal compositor)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
    }

    /// <summary>
    /// Starts a watcher. It gets a "commit" trace after each commit and a "frame-done" trace when frame callbacks go out, for each surface that passes the filter.
    /// </summary>
    /// <param name="filter">Selects the surfaces to trace.</param>
    /// <param name="sink">Receives each trace on the compositor thread.</param>
    /// <returns>A handle that stops the watcher when it is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> or <paramref name="sink"/> is <see langword="null"/>.</exception>
    public IDisposable WatchCommits(Func<Surface, bool> filter, Action<CommitTrace> sink)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(sink);
        var watcher = new Watcher(this, filter, sink);
        _watchers.Add(watcher);
        if (_watchers.Count == 1)
        {
            _compositor.SurfaceCreated += Hook;
            _compositor.FramesDone += OnFramesDone;
            foreach (var surface in _compositor.Surfaces)
            {
                Hook(surface);
            }
        }

        return watcher;
    }

    private void Hook(Surface surface)
    {
        if (_hooked.ContainsKey(surface))
        {
            return;
        }

        var hooks = new Hooks(() => OnCommitted(surface), () => Unhook(surface));
        surface.Committed += hooks.Committed;
        surface.Destroyed += hooks.Destroyed;
        _hooked[surface] = hooks;
    }

    private void Unhook(Surface surface)
    {
        if (_hooked.Remove(surface, out var hooks))
        {
            surface.Committed -= hooks.Committed;
            surface.Destroyed -= hooks.Destroyed;
        }
    }

    private void UnhookAll()
    {
        _compositor.SurfaceCreated -= Hook;
        _compositor.FramesDone -= OnFramesDone;
        foreach (var surface in _hooked.Keys.ToArray())
        {
            Unhook(surface);
        }
    }

    private void OnCommitted(Surface surface)
    {
        AllocationScope.Pause();
        try
        {
            var current = surface.Current;
            var damage = new List<Box>();
            foreach (var box in RegionRects.Of(current.BufferDamage))
            {
                damage.Add(new Box(box.X1, box.Y1, box.X2 - box.X1, box.Y2 - box.Y1));
            }

            foreach (var box in RegionRects.Of(current.SurfaceDamage))
            {
                damage.Add(new Box(box.X1, box.Y1, box.X2 - box.X1, box.Y2 - box.Y1));
            }

            var buffer = current.Buffer;
            Send(surface, new CommitTrace(
                "commit",
                surface,
                buffer?.Width ?? 0,
                buffer?.Height ?? 0,
                buffer?.Format ?? DrmFormat.Invalid,
                damage,
                current.FrameCallbacks.Count + current.FrameResources.Count,
                PresentationTimeGlobal.HasPendingFeedback(current),
                0));
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private void OnFramesDone(Surface surface, uint timestampMs, int count)
    {
        AllocationScope.Pause();
        try
        {
            Send(surface, new CommitTrace("frame-done", surface, 0, 0, DrmFormat.Invalid, [], count, false, timestampMs));
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private void Send(Surface surface, in CommitTrace trace)
    {
        foreach (var watcher in _watchers.ToArray())
        {
            if (watcher.Filter(surface))
            {
                watcher.Sink(trace);
            }
        }
    }

    private sealed record Hooks(Action Committed, Action Destroyed);

    private sealed class Watcher(SurfaceDiagnostics owner, Func<Surface, bool> filter, Action<CommitTrace> sink) : IDisposable
    {
        public Func<Surface, bool> Filter { get; } = filter;

        public Action<CommitTrace> Sink { get; } = sink;

        public void Dispose()
        {
            if (owner._watchers.Remove(this) && owner._watchers.Count == 0)
            {
                owner.UnhookAll();
            }
        }
    }
}
