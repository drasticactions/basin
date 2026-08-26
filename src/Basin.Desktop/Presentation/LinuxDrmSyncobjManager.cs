using System.Runtime.Versioning;
using Basin.Backend.Drm;
using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

[SupportedOSPlatform("linux")]
public sealed class LinuxDrmSyncobjManager : IDisposable
{
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

    public sealed record CommitSync(DrmSyncobjTimeline Acquire, ulong AcquirePoint, DrmSyncobjTimeline Release, ulong ReleasePoint);

    public LinuxDrmSyncobjManager(WlServerDisplay display, CompositorGlobal compositor, IDrmSyncDevice device)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(device);
        _compositor = compositor;
        _device = device;
        _global = display.CreateGlobal(WpLinuxDrmSyncobjManagerV1.Interface, Version, OnBind);
    }

    public void DeclareRenderer(IRenderer renderer)
    {
        if (renderer.FencePrecision == RenderFencePrecision.PerSubmission)
        {
            throw new NotSupportedException(
                "Release points are signaled at buffer replacement, which requires renderer reads to be complete by Submit. " +
                "A per-submission renderer must drive release points from its pass fences instead.");
        }
    }

    public long ExplicitCommits { get; private set; }

    public long ManagedCommits { get; private set; }

    public long LateAcquires { get; private set; }

    public long UnexportableAcquires { get; private set; }

    public long DeferredReleases { get; private set; }

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

    public void Dispose() => _global.Dispose();

    public CommitSync? CurrentOf(Surface surface) =>
        _surfaces.TryGetValue(surface, out var sync) ? sync.Current : null;

    public bool WaitAcquireCpu(Surface surface, long timeoutNs = 1_000_000_000)
    {
        var sync = CurrentOf(surface);
        return sync is null || sync.Acquire.Wait(sync.AcquirePoint, timeoutNs);
    }

    public bool AcquireReady(Surface surface)
    {
        var sync = CurrentOf(surface);
        return sync is null || sync.Acquire.IsSignaled(sync.AcquirePoint);
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpLinuxDrmSyncobjManagerV1Resource(client, version, id);
        manager.ImportTimeline += (_, e) =>
        {
            var resource = new WpLinuxDrmSyncobjTimelineV1Resource(client, manager.Version, e.Id);
            if (!_device.TryImportTimeline(e.Fd, out var timeline))
            {
                client.CloseFd(e.Fd);
                manager.PostError(ErrorInvalidTimeline, "fd is not a drm_syncobj");
                return;
            }

            client.CloseFd(e.Fd);
            var raw = resource.RawHandle;
            _timelines[raw] = timeline;
            resource.Destroyed += (_, _) =>
            {
                _timelines.Remove(raw);
                timeline.Release();
            };
        };
        manager.GetSurface += (_, e) =>
        {
            var resource = new WpLinuxDrmSyncobjSurfaceV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (_surfaces.ContainsKey(surface))
            {
                manager.PostError(ErrorSurfaceExists, "surface already has a sync object");
                return;
            }

            var sync = new SyncSurface(this, resource, surface);
            _surfaces[surface] = sync;
            resource.Destroyed += (_, _) =>
            {
                _surfaces.Remove(surface);
                sync.Drop();
            };
            surface.Destroyed += () => _surfaces.Remove(surface);
        };
    }

    private DrmSyncobjTimeline? TimelineOf(nint resourceHandle) => _timelines.GetValueOrDefault(resourceHandle);

    internal sealed class PendingSync : IDisposable
    {
        public PendingSync(DrmSyncobjTimeline acquire, ulong acquirePoint, DrmSyncobjTimeline release, ulong releasePoint)
        {
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

        public void Dispose()
        {
            Acquire.Release();
            Release.Release();
        }
    }

    private sealed class SyncSurface
    {
        private readonly LinuxDrmSyncobjManager _owner;
        private readonly WpLinuxDrmSyncobjSurfaceV1Resource _resource;
        private readonly Surface _surface;
        private (DrmSyncobjTimeline Timeline, ulong Point)? _pendingAcquire;
        private (DrmSyncobjTimeline Timeline, ulong Point)? _pendingRelease;

        public SyncSurface(LinuxDrmSyncobjManager owner, WpLinuxDrmSyncobjSurfaceV1Resource resource, Surface surface)
        {
            _owner = owner;
            _resource = resource;
            _surface = surface;

            resource.SetAcquirePoint += (_, e) => SetPoint(ref _pendingAcquire, e.Timeline?.RawHandle ?? 0, e.PointHi, e.PointLo);
            resource.SetReleasePoint += (_, e) => SetPoint(ref _pendingRelease, e.Timeline?.RawHandle ?? 0, e.PointHi, e.PointLo);
            surface.CommitRequested += OnCommitRequested;
            surface.Committed += OnCommitted;
        }

        public CommitSync? Current { get; private set; }

        public void Drop()
        {
            _surface.CommitRequested -= OnCommitRequested;
            _surface.Committed -= OnCommitted;
            ClearCurrent();
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
                        _resource.PostError(ErrorNoAcquirePoint, "buffer committed without an acquire point");
                    }
                }

                return;
            }

            if (!attachedBuffer)
            {
                _resource.PostError(ErrorNoBuffer, "sync points committed without a buffer");
                return;
            }

            if (acquireSlot is not { } acquire)
            {
                _resource.PostError(ErrorNoAcquirePoint, "missing acquire point");
                return;
            }

            if (releaseSlot is not { } release)
            {
                _resource.PostError(ErrorNoReleasePoint, "missing release point");
                return;
            }

            if (acquire.Timeline == release.Timeline && release.Point <= acquire.Point)
            {
                _resource.PostError(ErrorConflictingPoints, "release point not after acquire point on the same timeline");
                return;
            }

            _owner.ManagedCommits++;
            _owner.ExplicitCommits++;
            _surface.Pending.SetExtension(new PendingSync(acquire.Timeline, acquire.Point, release.Timeline, release.Point));
        }

        private void OnCommitted()
        {
            if (_surface.Current.TakeExtension<PendingSync>() is not { } sync)
            {
                return;
            }

            using (sync)
            {
                ClearCurrent();
                sync.Acquire.Retain();
                sync.Release.Retain();
                Current = new CommitSync(sync.Acquire, sync.AcquirePoint, sync.Release, sync.ReleasePoint);

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

                if (_surface.Current.Buffer is { } buffer)
                {
                    var releaseTimeline = sync.Release;
                    var releasePoint = sync.ReleasePoint;
                    releaseTimeline.Retain();
                    Action? handler = null;
                    handler = () =>
                    {
                        buffer.Released -= handler;
                        _owner.SignalRelease(buffer, releaseTimeline, releasePoint);
                        releaseTimeline.Release();
                    };
                    buffer.Released += handler;
                }
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
