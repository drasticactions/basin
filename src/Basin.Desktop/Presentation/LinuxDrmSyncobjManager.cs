using System.Runtime.Versioning;
using Basin.Backend.Drm;
using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_linux_drm_syncobj_manager_v1</c> global. It waits on acquire points before a commit applies, and signals release points when the buffer is free.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxDrmSyncobjManager : IDisposable, IExplicitSyncDiagnostics, IWpLinuxDrmSyncobjManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorSurfaceExists = 0;
    private const uint ErrorInvalidTimeline = 1;
    private const uint ErrorNoBuffer = 3;
    private const uint ErrorNoAcquirePoint = 4;
    private const uint ErrorNoReleasePoint = 5;
    private const uint ErrorConflictingPoints = 6;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IDrmSyncDevice _device;
    private readonly Dictionary<nint, DrmSyncobjTimeline> _timelines = [];
    private readonly Dictionary<Surface, SyncSurface> _surfaces = [];
    private readonly Dictionary<Surface, SyncSurface> _dropped = [];

    /// <summary>
    /// Holds the explicit sync points of the current buffer of a surface.
    /// </summary>
    /// <param name="Acquire">The timeline that signals when the buffer is ready.</param>
    /// <param name="AcquirePoint">The point on <paramref name="Acquire"/> to wait for.</param>
    /// <param name="Release">The timeline to signal when the compositor is done with the buffer.</param>
    /// <param name="ReleasePoint">The point on <paramref name="Release"/> to signal.</param>
    public sealed record CommitSync(DrmSyncobjTimeline Acquire, ulong AcquirePoint, DrmSyncobjTimeline Release, ulong ReleasePoint);

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxDrmSyncobjManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces get sync state.</param>
    /// <param name="device">The DRM device that imports the timelines.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> or <paramref name="device"/> is <see langword="null"/>.</exception>
    public LinuxDrmSyncobjManager(WlServerDisplay display, CompositorGlobal compositor, IDrmSyncDevice device)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(device);
        _compositor = compositor;
        _device = device;
        _global = display.CreateGlobal(WpLinuxDrmSyncobjManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Checks that the renderer completes its reads by submit, because release points signal when the buffer is replaced.
    /// </summary>
    /// <param name="renderer">The renderer that reads the client buffers.</param>
    /// <exception cref="NotSupportedException">The renderer has <see cref="RenderFencePrecision.PerSubmission"/> fences.</exception>
    public void DeclareRenderer(IRenderer renderer)
    {
        if (renderer.FencePrecision == RenderFencePrecision.PerSubmission)
        {
            throw new NotSupportedException(
                "The renderer has per-submission fences, but release points signal when the buffer is replaced. " +
                "A per-submission renderer must signal release points from its pass fences.");
        }
    }

    /// <summary>
    /// Gets the number of commits that carried both an acquire and a release point.
    /// </summary>
    public long ExplicitCommits { get; private set; }

    /// <summary>
    /// Gets the number of buffer commits on surfaces with a syncobj object, with or without explicit points.
    /// </summary>
    public long ManagedCommits { get; private set; }

    /// <summary>
    /// Gets the number of acquire points that had no fence to export when the commit applied, so the manager waited on the CPU.
    /// </summary>
    public long LateAcquires { get; private set; }

    /// <summary>
    /// Gets the number of commits that waited on the loop for an unsignaled acquire point.
    /// </summary>
    public long ParkedAcquires { get; private set; }

    /// <summary>
    /// Gets the number of unsignaled acquire points that the manager cannot wait for on the loop, so the commit applies at once.
    /// </summary>
    public long UnparkedAcquires { get; private set; }

    /// <summary>
    /// Gets the number of acquire points that still had no fence to export after the CPU wait.
    /// </summary>
    public long UnexportableAcquires { get; private set; }

    /// <summary>
    /// Gets the number of release points that waited for outstanding renderer reads of the buffer.
    /// </summary>
    public long DeferredReleases { get; private set; }

    /// <summary>
    /// Gets the number of release points of buffers that a later synchronized commit replaced before they reached the screen.
    /// </summary>
    public long SupersededReleases { get; private set; }

    /// <summary>
    /// Gets the number of release points of commits that were discarded before they applied, for example with a destroyed surface.
    /// </summary>
    public long DiscardedReleases { get; private set; }

    /// <inheritdoc/>
    public ExplicitSyncCounters Counters => new(
        ManagedCommits, ExplicitCommits, ParkedAcquires, UnparkedAcquires, LateAcquires, UnexportableAcquires, DeferredReleases,
        SupersededReleases, DiscardedReleases);

    private void SignalRelease(IBuffer buffer, DrmSyncobjTimeline timeline, ulong point)
    {
        var reads = OutstandingReads(buffer);
        if (reads < 0)
        {
            timeline.Signal(point);
            return;
        }

        DeferredReleases++;
        if (!timeline.ImportSyncFileAt(point, reads))
        {
            timeline.Signal(point);
        }

        RenderFences.CloseFence(reads);
    }

    private void SignalWhenFree(IBuffer buffer, DrmSyncobjTimeline timeline, ulong point)
    {
        if (buffer.LockCount > 0)
        {
            SignalOnRelease(buffer, timeline, point);
        }
        else if (buffer.IsDestroyed)
        {
            timeline.Signal(point);
        }
        else
        {
            SignalRelease(buffer, timeline, point);
        }
    }

    private void SignalOnRelease(IBuffer buffer, DrmSyncobjTimeline timeline, ulong point)
    {
        timeline.Retain();
        Action? handler = null;
        handler = () =>
        {
            buffer.Released -= handler;
            SignalRelease(buffer, timeline, point);
            timeline.Release();
        };
        buffer.Released += handler;
    }

    private static int OutstandingReads(IBuffer buffer)
    {
        if (!buffer.TryGetDmabuf(out var attributes))
        {
            return -1;
        }

        var merged = -1;
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            var fence = RenderFences.ExportDmabufSyncFile(attributes.Fds[plane], forWrite: true);
            if (fence < 0)
            {
                continue;
            }

            if (RenderFences.WaitSyncFile(fence, 0))
            {
                RenderFences.CloseFence(fence);
                continue;
            }

            if (merged < 0)
            {
                merged = fence;
                continue;
            }

            var combined = RenderFences.MergeSyncFiles(merged, fence);
            RenderFences.CloseFence(fence);
            if (combined >= 0)
            {
                RenderFences.CloseFence(merged);
                merged = combined;
            }
        }

        return merged;
    }

    private const long MaterializeTimeoutNs = 100_000_000;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Gets the sync points of the surface's current buffer.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The sync points, or <see langword="null"/> when the current buffer has no explicit sync.</returns>
    public CommitSync? CurrentOf(Surface surface) =>
        _surfaces.TryGetValue(surface, out var sync) ? sync.Current : null;

    /// <summary>
    /// Blocks the calling thread until the acquire point of the surface's current buffer signals.
    /// </summary>
    /// <param name="surface">The surface whose buffer to wait for.</param>
    /// <param name="timeoutNs">The longest wait in nanoseconds.</param>
    /// <returns><see langword="true"/> when the point signaled or there is no explicit sync, or <see langword="false"/> on timeout.</returns>
    public bool WaitAcquireCpu(Surface surface, long timeoutNs = 1_000_000_000)
    {
        var sync = CurrentOf(surface);
        return sync is null || sync.Acquire.Wait(sync.AcquirePoint, timeoutNs);
    }

    /// <summary>
    /// Tests without blocking whether the acquire point of the surface's current buffer has signaled.
    /// </summary>
    /// <param name="surface">The surface to test.</param>
    /// <returns><see langword="true"/> when the point signaled or there is no explicit sync.</returns>
    public bool AcquireReady(Surface surface)
    {
        var sync = CurrentOf(surface);
        return sync is null || sync.Acquire.IsSignaled(sync.AcquirePoint);
    }

    void IWpLinuxDrmSyncobjManagerV1Handler.Destroy(WpLinuxDrmSyncobjManagerV1Resource resource)
    {
    }

    void IWpLinuxDrmSyncobjManagerV1Handler.ImportTimeline(WpLinuxDrmSyncobjManagerV1Resource resource, in WpLinuxDrmSyncobjManagerV1Resource.ImportTimelineEventArgs args)
    {
        var client = resource.Client;
        var timelineResource = new WpLinuxDrmSyncobjTimelineV1Resource(client, resource.Version, args.Id);
        if (!_device.TryImportTimeline(args.Fd, out var timeline))
        {
            client.CloseFd(args.Fd);
            resource.PostError(ErrorInvalidTimeline, "The fd is not a drm_syncobj.");
            return;
        }

        client.CloseFd(args.Fd);
        var raw = timelineResource.RawHandle;
        _timelines[raw] = timeline;
        timelineResource.Destroyed += (_, _) =>
        {
            _timelines.Remove(raw);
            timeline.Release();
        };
    }

    void IWpLinuxDrmSyncobjManagerV1Handler.GetSurface(WpLinuxDrmSyncobjManagerV1Resource resource, in WpLinuxDrmSyncobjManagerV1Resource.GetSurfaceEventArgs args)
    {
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            _ = new WpLinuxDrmSyncobjSurfaceV1Resource(resource.Client, resource.Version, args.Id, IgnoredSyncSurface.Instance);
            return;
        }

        if (_surfaces.ContainsKey(surface))
        {
            _ = new WpLinuxDrmSyncobjSurfaceV1Resource(resource.Client, resource.Version, args.Id, IgnoredSyncSurface.Instance);
            resource.PostError(ErrorSurfaceExists, "The surface already has a sync object.");
            return;
        }

        if (_dropped.Remove(surface, out var previous))
        {
            previous.Detach();
        }

        var sync = new SyncSurface(this, resource.Client, resource.Version, args.Id, surface);
        _surfaces[surface] = sync;
        sync.Resource.Destroyed += (_, _) =>
        {
            if (_surfaces.TryGetValue(surface, out var live) && ReferenceEquals(live, sync))
            {
                _surfaces.Remove(surface);
            }

            sync.Drop();
            if (!surface.IsDestroyed && !_surfaces.ContainsKey(surface))
            {
                _dropped[surface] = sync;
            }
            else
            {
                sync.Detach();
            }
        };
        surface.Destroyed += () =>
        {
            _surfaces.Remove(surface);
            if (_dropped.Remove(surface, out var dropped))
            {
                dropped.Detach();
            }
        };
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new WpLinuxDrmSyncobjManagerV1Resource(client, version, id, this);

    private DrmSyncobjTimeline? TimelineOf(nint resourceHandle) => _timelines.GetValueOrDefault(resourceHandle);

    internal sealed class PendingSync : IDisposable, ISupersededPayload
    {
        private readonly LinuxDrmSyncobjManager _owner;

        public PendingSync(
            LinuxDrmSyncobjManager owner, IBuffer buffer, DrmSyncobjTimeline acquire, ulong acquirePoint, DrmSyncobjTimeline release, ulong releasePoint)
        {
            _owner = owner;
            Buffer = buffer;
            acquire.Retain();
            release.Retain();
            Acquire = acquire;
            AcquirePoint = acquirePoint;
            Release = release;
            ReleasePoint = releasePoint;
        }

        public DrmSyncobjTimeline Acquire { get; }

        public ulong AcquirePoint { get; }

        public DrmSyncobjTimeline Release { get; }

        public ulong ReleasePoint { get; }

        public IBuffer? Buffer { get; private set; }

        public void Superseded()
        {
            if (Buffer is null)
            {
                return;
            }

            _owner.SupersededReleases++;
            ReleaseWhenFree();
        }

        public void ReleaseWhenFree()
        {
            if (Buffer is { } buffer)
            {
                Buffer = null;
                _owner.SignalWhenFree(buffer, Release, ReleasePoint);
            }
        }

        public void Dispose()
        {
            if (Buffer is not null)
            {
                _owner.DiscardedReleases++;
                ReleaseWhenFree();
            }

            Acquire.Release();
            Release.Release();
        }
    }

    private sealed class IgnoredSyncSurface : IWpLinuxDrmSyncobjSurfaceV1Handler
    {
        public static readonly IgnoredSyncSurface Instance = new();

        void IWpLinuxDrmSyncobjSurfaceV1Handler.Destroy(WpLinuxDrmSyncobjSurfaceV1Resource resource)
        {
        }

        void IWpLinuxDrmSyncobjSurfaceV1Handler.SetAcquirePoint(WpLinuxDrmSyncobjSurfaceV1Resource resource, in WpLinuxDrmSyncobjSurfaceV1Resource.SetAcquirePointEventArgs args)
        {
        }

        void IWpLinuxDrmSyncobjSurfaceV1Handler.SetReleasePoint(WpLinuxDrmSyncobjSurfaceV1Resource resource, in WpLinuxDrmSyncobjSurfaceV1Resource.SetReleasePointEventArgs args)
        {
        }
    }

    private sealed class SyncSurface : IWpLinuxDrmSyncobjSurfaceV1Handler
    {
        private readonly LinuxDrmSyncobjManager _owner;
        private readonly WpLinuxDrmSyncobjSurfaceV1Resource _resource;
        private readonly Surface _surface;
        private (DrmSyncobjTimeline Timeline, ulong Point)? _pendingAcquire;
        private (DrmSyncobjTimeline Timeline, ulong Point)? _pendingRelease;
        private bool _dropped;

        public SyncSurface(LinuxDrmSyncobjManager owner, WlClient client, uint version, uint id, Surface surface)
        {
            _owner = owner;
            _surface = surface;
            _resource = new WpLinuxDrmSyncobjSurfaceV1Resource(client, version, id, this);

            surface.CommitRequested += OnCommitRequested;
            surface.Committed += OnCommitted;
        }

        public WpLinuxDrmSyncobjSurfaceV1Resource Resource => _resource;

        public CommitSync? Current { get; private set; }

        void IWpLinuxDrmSyncobjSurfaceV1Handler.Destroy(WpLinuxDrmSyncobjSurfaceV1Resource resource)
        {
        }

        void IWpLinuxDrmSyncobjSurfaceV1Handler.SetAcquirePoint(WpLinuxDrmSyncobjSurfaceV1Resource resource, in WpLinuxDrmSyncobjSurfaceV1Resource.SetAcquirePointEventArgs args) =>
            SetPoint(ref _pendingAcquire, args.Timeline?.RawHandle ?? 0, args.PointHi, args.PointLo);

        void IWpLinuxDrmSyncobjSurfaceV1Handler.SetReleasePoint(WpLinuxDrmSyncobjSurfaceV1Resource resource, in WpLinuxDrmSyncobjSurfaceV1Resource.SetReleasePointEventArgs args) =>
            SetPoint(ref _pendingRelease, args.Timeline?.RawHandle ?? 0, args.PointHi, args.PointLo);

        public void Drop()
        {
            _dropped = true;
            _surface.CommitRequested -= OnCommitRequested;
            ClearCurrent();
        }

        public void Detach()
        {
            Drop();
            _surface.Committed -= OnCommitted;
        }

        private void SetPoint(ref (DrmSyncobjTimeline, ulong)? slot, nint timelineHandle, uint hi, uint lo)
        {
            if (_owner.TimelineOf(timelineHandle) is { } timeline)
            {
                slot = (timeline, ((ulong)hi << 32) | lo);
            }
        }

        private void OnCommitRequested()
        {
            var acquireSlot = _pendingAcquire;
            var releaseSlot = _pendingRelease;
            _pendingAcquire = null;
            _pendingRelease = null;

            var attachedBuffer = (_surface.Pending.Committed & SurfaceStateFields.Buffer) != 0 &&
                _surface.Pending.Buffer is not null;
            if (acquireSlot is null && releaseSlot is null)
            {
                if (attachedBuffer)
                {
                    _owner.ManagedCommits++;
                    if (!_resource.IsDestroyed)
                    {
                        _resource.PostError(ErrorNoAcquirePoint, "A buffer was committed without an acquire point.");
                    }
                }

                return;
            }

            if (!attachedBuffer)
            {
                _resource.PostError(ErrorNoBuffer, "Sync points were committed without a buffer.");
                return;
            }

            if (acquireSlot is not { } acquire)
            {
                _resource.PostError(ErrorNoAcquirePoint, "The commit has no acquire point.");
                return;
            }

            if (releaseSlot is not { } release)
            {
                _resource.PostError(ErrorNoReleasePoint, "The commit has no release point.");
                return;
            }

            if (acquire.Timeline == release.Timeline && release.Point <= acquire.Point)
            {
                _resource.PostError(ErrorConflictingPoints, "The release point must come after the acquire point on the same timeline.");
                return;
            }

            _owner.ManagedCommits++;
            _owner.ExplicitCommits++;
            _surface.MarkExplicitAcquire();
            _surface.Pending.SetExtension(new PendingSync(_owner, _surface.Pending.Buffer!, acquire.Timeline, acquire.Point, release.Timeline, release.Point));
            ParkOnAcquire(acquire.Timeline, acquire.Point);
        }

        private void ParkOnAcquire(DrmSyncobjTimeline timeline, ulong point)
        {
            if (timeline.IsSignaled(point))
            {
                return;
            }

            if (_owner._compositor.Loop is { } loop && SyncobjCommitFence.TryCreate(loop, timeline, point) is { } fence)
            {
                _owner.ParkedAcquires++;
                _surface.ParkFence(fence);
                return;
            }

            _owner.UnparkedAcquires++;
        }

        private void OnCommitted()
        {
            if (_surface.Current.TakeExtension<PendingSync>() is not { } sync)
            {
                return;
            }

            using (sync)
            {
                if (!_dropped)
                {
                    ClearCurrent();
                    sync.Acquire.Retain();
                    sync.Release.Retain();
                    Current = new CommitSync(sync.Acquire, sync.AcquirePoint, sync.Release, sync.ReleasePoint);
                }

                var exported = sync.Acquire.ExportSyncFileAt(sync.AcquirePoint);
                if (exported < 0)
                {
                    _owner.LateAcquires++;
                    _ = sync.Acquire.Wait(sync.AcquirePoint, MaterializeTimeoutNs);
                    exported = sync.Acquire.ExportSyncFileAt(sync.AcquirePoint);
                    if (exported < 0)
                    {
                        _owner.UnexportableAcquires++;
                    }
                }

                _surface.SetAcquireFence(exported);
                sync.ReleaseWhenFree();
            }
        }

        private void ClearCurrent()
        {
            if (Current is { } current)
            {
                current.Acquire.Release();
                current.Release.Release();
                Current = null;
            }
        }
    }
}
