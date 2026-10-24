using System.Runtime.InteropServices;
using Basin.Backend.Drm;
using Basin.Desktop;
using Basin.Desktop.Protocol;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SupersededReleaseTests : IDisposable
{
    private const long Second = 1_000_000_000;

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly int _drmFd;
    private readonly List<DrmSyncobjTimeline> _timelines = [];

    public SupersededReleaseTests()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "explicit sync is DRM syncobjs, which are a Linux object");
        _drmFd = open(CompositorTestHost.RenderNodePath, 2);
    }

    public void Dispose()
    {
        foreach (var timeline in _timelines)
        {
            timeline.Release();
        }

        if (_drmFd >= 0)
        {
            close(_drmFd);
        }
    }

    private void RequireDrm()
    {
        Assert.SkipWhen(_drmFd < 0, "no render node available");
        var probe = DrmSyncobjTimeline.TryCreate(_drmFd);
        Assert.SkipWhen(probe is null, $"{CompositorTestHost.RenderNodePath} has no syncobj support");
        probe!.Release();
    }

    [Fact]
    public void A_superseded_cached_buffer_gets_its_release_point_before_the_parent_commits()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var parent = MapParent(host);
        var (child, _) = Child(host, parent, synchronized: true);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var release = sync.Timeline();
        host.PumpToServer();

        acquire.Timeline.Signal(3);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        CommitSynced(child, syncSurface, a, acquire.Proxy, 1, release.Proxy, 2);
        CommitSynced(child, syncSurface, b, acquire.Proxy, 3, release.Proxy, 4);
        host.PumpToServer();

        Assert.True(release.Timeline.Wait(2, Second), "the release point of a buffer that a cached commit replaced must signal at once");
        Assert.False(release.Timeline.IsSignaled(4));
        Assert.Equal(1, sync.Manager.SupersededReleases);

        parent.Commit();
        host.PumpToServer();
        Assert.False(release.Timeline.IsSignaled(4), "the buffer on screen keeps its release point");

        acquire.Timeline.Signal(5);
        var c = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFFFF0000));
        CommitSynced(child, syncSurface, c, acquire.Proxy, 5, release.Proxy, 6);
        parent.Commit();
        host.PumpToServer();
        Assert.True(release.Timeline.Wait(4, Second));
        Assert.False(release.Timeline.IsSignaled(6));
        Assert.Equal(1, sync.Manager.SupersededReleases);

        syncSurface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_superseded_cached_buffer_signals_its_own_timeline()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var parent = MapParent(host);
        var (child, _) = Child(host, parent, synchronized: true);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var releaseA = sync.Timeline();
        var releaseB = sync.Timeline();
        host.PumpToServer();

        acquire.Timeline.Signal(2);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        CommitSynced(child, syncSurface, a, acquire.Proxy, 1, releaseA.Proxy, 1);
        CommitSynced(child, syncSurface, b, acquire.Proxy, 2, releaseB.Proxy, 1);
        host.PumpToServer();

        Assert.True(releaseA.Timeline.Wait(1, Second), "a per-image timeline gets no later point that completes the superseded one");
        Assert.False(releaseB.Timeline.IsSignaled(1));

        parent.Commit();
        host.PumpToServer();
        Assert.False(releaseB.Timeline.IsSignaled(1));

        syncSurface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_superseded_cached_buffer_gets_wl_buffer_release()
    {
        using var host = new CompositorTestHost();
        var parent = MapParent(host);
        var (child, _) = Child(host, parent, synchronized: true);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        a.TrackRelease();
        b.TrackRelease();

        child.Attach(a.Proxy, 0, 0);
        child.Damage(0, 0, 20, 20);
        child.Commit();
        child.Attach(b.Proxy, 0, 0);
        child.Damage(0, 0, 20, 20);
        child.Commit();
        host.PumpToServer();
        host.PumpUntil(() => a.Released);
        Assert.False(b.Released);

        parent.Commit();
        host.PumpToServer();
        host.PumpToClient();
        Assert.False(b.Released);
    }

    [Fact]
    public void A_superseded_cached_buffer_fires_its_release_callback()
    {
        using var host = new CompositorTestHost();
        var parent = MapParent(host);
        var (child, _) = Child(host, parent, synchronized: true);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        var releasedA = 0;
        var releasedB = 0;

        child.Attach(a.Proxy, 0, 0);
        child.Damage(0, 0, 20, 20);
        child.GetRelease().Done += (_, _) => releasedA++;
        child.Commit();
        child.Attach(b.Proxy, 0, 0);
        child.Damage(0, 0, 20, 20);
        child.GetRelease().Done += (_, _) => releasedB++;
        child.Commit();
        host.PumpToServer();
        host.PumpUntil(() => releasedA == 1);

        parent.Commit();
        host.PumpToServer();
        host.PumpToClient();
        Assert.Equal(1, releasedA);
        Assert.Equal(0, releasedB);
    }

    [Fact]
    public void Parked_commits_that_land_in_one_pass_signal_the_superseded_release_point()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var parent = MapParent(host);
        var (child, _) = Child(host, parent, synchronized: true);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var releaseA = sync.Timeline();
        var releaseB = sync.Timeline();
        host.PumpToServer();

        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        CommitSynced(child, syncSurface, a, acquire.Proxy, 1, releaseA.Proxy, 1);
        CommitSynced(child, syncSurface, b, acquire.Proxy, 2, releaseB.Proxy, 1);
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single(s => s.SubsurfaceRole is not null);
        Assert.True(server.HasParkedCommits);
        Assert.Equal(2, sync.Manager.ParkedAcquires);

        acquire.Timeline.Signal(2);
        PumpUntil(host, () => !server.HasParkedCommits);

        Assert.True(releaseA.Timeline.Wait(1, Second), "the first parked commit was replaced in the cache before the parent committed");
        Assert.False(releaseB.Timeline.IsSignaled(1));
        Assert.Equal(1, sync.Manager.SupersededReleases);

        syncSurface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Parked_commits_on_a_toplevel_each_apply_and_release_the_one_before()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var window = MappedToplevel.Map(host, host.Client);
        var syncSurface = sync.Proxy.GetSurface(window.Surface);
        var acquire = sync.Timeline();
        var releaseA = sync.Timeline();
        var releaseB = sync.Timeline();
        host.PumpToServer();

        var a = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF0000FF));
        var b = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF00FF00));
        CommitSynced(window.Surface, syncSurface, a, acquire.Proxy, 1, releaseA.Proxy, 1);
        CommitSynced(window.Surface, syncSurface, b, acquire.Proxy, 2, releaseB.Proxy, 1);
        host.PumpToServer();
        Assert.True(window.ServerSurface.HasParkedCommits);

        acquire.Timeline.Signal(2);
        PumpUntil(host, () => !window.ServerSurface.HasParkedCommits);

        Assert.True(releaseA.Timeline.Wait(1, Second));
        Assert.False(releaseB.Timeline.IsSignaled(1));
        Assert.Equal(0, sync.Manager.SupersededReleases);

        syncSurface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_desynchronized_subsurface_releases_through_the_normal_path()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var parent = MapParent(host);
        var (child, _) = Child(host, parent, synchronized: false);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var release = sync.Timeline();
        host.PumpToServer();

        acquire.Timeline.Signal(2);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        CommitSynced(child, syncSurface, a, acquire.Proxy, 1, release.Proxy, 1);
        host.PumpToServer();
        Assert.False(release.Timeline.IsSignaled(1));

        CommitSynced(child, syncSurface, b, acquire.Proxy, 2, release.Proxy, 2);
        host.PumpToServer();

        Assert.True(release.Timeline.Wait(1, Second));
        Assert.False(release.Timeline.IsSignaled(2));
        Assert.Equal(0, sync.Manager.SupersededReleases);

        syncSurface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_superseded_buffer_that_is_still_on_screen_keeps_its_release_point()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var parent = MapParent(host);
        var (child, _) = Child(host, parent, synchronized: true);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var release = sync.Timeline();
        host.PumpToServer();

        acquire.Timeline.Signal(3);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        CommitSynced(child, syncSurface, a, acquire.Proxy, 1, release.Proxy, 1);
        parent.Commit();
        host.PumpToServer();

        CommitSynced(child, syncSurface, a, acquire.Proxy, 2, release.Proxy, 2);
        CommitSynced(child, syncSurface, b, acquire.Proxy, 3, release.Proxy, 3);
        host.PumpToServer();
        Assert.False(release.Timeline.IsSignaled(1), "buffer A is on screen, so no point of A may signal");
        Assert.Equal(1, sync.Manager.SupersededReleases);

        parent.Commit();
        host.PumpToServer();
        Assert.True(release.Timeline.Wait(2, Second), "once A leaves the screen, both of its points must signal");
        Assert.False(release.Timeline.IsSignaled(3));

        syncSurface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_cached_commit_whose_sync_object_is_destroyed_still_releases_its_buffer()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var parent = MapParent(host);
        var (child, _) = Child(host, parent, synchronized: true);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var release = sync.Timeline();
        host.PumpToServer();

        acquire.Timeline.Signal(1);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        a.TrackRelease();
        CommitSynced(child, syncSurface, a, acquire.Proxy, 1, release.Proxy, 1);
        syncSurface.Dispose();
        parent.Commit();
        host.PumpToServer();

        child.Attach(b.Proxy, 0, 0);
        child.Damage(0, 0, 20, 20);
        child.Commit();
        parent.Commit();
        host.PumpToServer();
        host.PumpUntil(() => a.Released);
        Assert.True(release.Timeline.Wait(1, Second), "a commit that applies after its sync object is gone still gets its release point");
    }

    [Fact]
    public void A_destroyed_subsurface_signals_the_release_point_of_its_cached_commit()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var parent = MapParent(host);
        var (child, subsurface) = Child(host, parent, synchronized: true);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var release = sync.Timeline();
        host.PumpToServer();

        acquire.Timeline.Signal(1);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        CommitSynced(child, syncSurface, a, acquire.Proxy, 1, release.Proxy, 1);
        host.PumpToServer();
        Assert.False(release.Timeline.IsSignaled(1));

        syncSurface.Dispose();
        subsurface.Dispose();
        child.Destroy();
        host.PumpToServer();

        Assert.True(release.Timeline.Wait(1, Second), "a cached commit that will never apply must give its buffer back");
        Assert.Equal(1, sync.Manager.DiscardedReleases);
    }

    [Fact]
    public void A_destroyed_surface_signals_the_release_point_of_its_parked_commit()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var window = MappedToplevel.Map(host, host.Client);
        var syncSurface = sync.Proxy.GetSurface(window.Surface);
        var acquire = sync.Timeline();
        var release = sync.Timeline();
        host.PumpToServer();

        var a = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF0000FF));
        CommitSynced(window.Surface, syncSurface, a, acquire.Proxy, 1, release.Proxy, 1);
        host.PumpToServer();
        Assert.True(window.ServerSurface.HasParkedCommits);

        syncSurface.Dispose();
        window.Toplevel.Dispose();
        window.XdgSurface.Dispose();
        window.Surface.Destroy();
        host.PumpToServer();

        Assert.True(release.Timeline.Wait(1, Second));
        Assert.Equal(1, sync.Manager.DiscardedReleases);
    }

    [Fact]
    public void Set_desync_applies_the_cache_and_the_applied_buffer_releases_normally()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var sync = new SyncClient(this, host);
        var parent = MapParent(host);
        var (child, subsurface) = Child(host, parent, synchronized: true);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var releaseA = sync.Timeline();
        var releaseB = sync.Timeline();
        var releaseC = sync.Timeline();
        host.PumpToServer();

        acquire.Timeline.Signal(3);
        var a = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF0000FF));
        var b = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFF00FF00));
        var c = host.Client.CreateBuffer(20, 20, Fill.Solid(20, 20, 0xFFFF0000));
        CommitSynced(child, syncSurface, a, acquire.Proxy, 1, releaseA.Proxy, 1);
        CommitSynced(child, syncSurface, b, acquire.Proxy, 2, releaseB.Proxy, 1);
        host.PumpToServer();
        Assert.True(releaseA.Timeline.Wait(1, Second));

        subsurface.SetDesync();
        host.PumpToServer();
        var server = host.Compositor.Surfaces.Single(s => s.SubsurfaceRole is not null);
        Assert.Equal(20, server.Current.Width);
        Assert.False(releaseB.Timeline.IsSignaled(1), "set_desync shows the cached buffer, so it is on screen");

        CommitSynced(child, syncSurface, c, acquire.Proxy, 3, releaseC.Proxy, 1);
        host.PumpToServer();
        Assert.True(releaseB.Timeline.Wait(1, Second));
        Assert.False(releaseC.Timeline.IsSignaled(1));
        Assert.Equal(1, sync.Manager.SupersededReleases);

        syncSurface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_superseded_dmabuf_that_the_gpu_read_releases_after_it_leaves_the_screen()
    {
        RequireDrm();
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var host = new CompositorTestHost(renderer: "gl");
        using var device = new Basin.Render.Gl.GlDevice(CompositorTestHost.RenderNodePath);
        using var allocator = device.CreateAllocator();
        var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Argb8888).ToArray();
        var allocated = allocator.Allocate(64, 64, DrmFormat.Argb8888, modifiers, BufferUse.Render);
        Assert.SkipWhen(allocated is null, "the device would not allocate a renderable buffer");
        Assert.True(allocated!.TryGetDmabuf(out var planes));

        using var sync = new SyncClient(this, host);
        var window = MappedToplevel.Map(host, host.Client);
        var (child, _) = Child(host, window.Surface, synchronized: true);
        var syncSurface = sync.Proxy.GetSurface(child);
        var acquire = sync.Timeline();
        var releaseShown = sync.Timeline();
        var releaseAgain = sync.Timeline();
        var releaseB = sync.Timeline();
        host.PumpToServer();

        var parameters = host.Client.Dmabuf!.CreateParams();
        parameters.Add(
            planes.Fds[0], 0, (uint)planes.Offsets[0], (uint)planes.Strides[0],
            (uint)(planes.Modifier >> 32), (uint)(planes.Modifier & 0xFFFFFFFF));
        WlBuffer? created = null;
        var refused = false;
        parameters.Created += (_, e) => created = e.Buffer;
        parameters.Failed += (_, _) => refused = true;
        parameters.Create(64, 64, (uint)DrmFormat.Argb8888, 0);
        host.PumpUntil(() => created is not null || refused);
        Assert.SkipWhen(refused, "the compositor would not take a buffer from this device");

        acquire.Timeline.Signal(3);
        CommitDmabuf(child, syncSurface, created!, acquire.Proxy, 1, releaseShown.Proxy);
        window.Surface.Commit();
        host.PumpToServer();
        host.RenderFrame();

        CommitDmabuf(child, syncSurface, created!, acquire.Proxy, 2, releaseAgain.Proxy);
        var b = host.Client.CreateBuffer(64, 64, Fill.Solid(64, 64, 0xFF00FF00));
        CommitSynced(child, syncSurface, b, acquire.Proxy, 3, releaseB.Proxy, 1);
        host.PumpToServer();
        Assert.False(releaseShown.Timeline.IsSignaled(1));
        Assert.False(releaseAgain.Timeline.IsSignaled(1), "the dmabuf is still on screen");

        window.Surface.Commit();
        host.PumpToServer();
        host.RenderFrame();
        Assert.True(releaseShown.Timeline.Wait(1, Second));
        Assert.True(releaseAgain.Timeline.Wait(1, Second));
        Assert.False(releaseB.Timeline.IsSignaled(1));

        syncSurface.Dispose();
        created!.Dispose();
        parameters.Dispose();
        host.PumpToServer();
        (allocated as BufferBase)?.Destroy();
    }

    private static void CommitDmabuf(
        WlSurface surface, WpLinuxDrmSyncobjSurfaceV1 syncSurface, WlBuffer buffer,
        WpLinuxDrmSyncobjTimelineV1 acquire, ulong acquirePoint, WpLinuxDrmSyncobjTimelineV1 release)
    {
        syncSurface.SetAcquirePoint(acquire, 0, (uint)acquirePoint);
        syncSurface.SetReleasePoint(release, 0, 1);
        surface.Attach(buffer, 0, 0);
        surface.Damage(0, 0, 64, 64);
        surface.Commit();
    }

    private static void CommitSynced(
        WlSurface surface, WpLinuxDrmSyncobjSurfaceV1 syncSurface, ClientShmBuffer buffer,
        WpLinuxDrmSyncobjTimelineV1 acquire, ulong acquirePoint, WpLinuxDrmSyncobjTimelineV1 release, ulong releasePoint)
    {
        syncSurface.SetAcquirePoint(acquire, (uint)(acquirePoint >> 32), (uint)acquirePoint);
        syncSurface.SetReleasePoint(release, (uint)(releasePoint >> 32), (uint)releasePoint);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, buffer.Width, buffer.Height);
        surface.Commit();
    }

    private static WlSurface MapParent(CompositorTestHost host)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(60, 60, Fill.Solid(60, 60, 0xFFCC0000));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 60, 60);
        surface.Commit();
        host.PumpToServer();
        return surface;
    }

    private static (WlSurface Surface, WlSubsurface Subsurface) Child(CompositorTestHost host, WlSurface parent, bool synchronized)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(surface, parent);
        if (!synchronized)
        {
            subsurface.SetDesync();
        }

        host.PumpToServer();
        return (surface, subsurface);
    }

    private static void PumpUntil(CompositorTestHost host, Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            host.Loop.Dispatch(10);
        }

        Assert.True(condition(), "condition not reached within 5 s");
    }

    private sealed class SyncClient : IDisposable
    {
        private readonly SupersededReleaseTests _owner;
        private readonly CompositorTestHost _host;
        private readonly List<WpLinuxDrmSyncobjTimelineV1> _proxies = [];

        public SyncClient(SupersededReleaseTests owner, CompositorTestHost host)
        {
            _owner = owner;
            _host = host;
            Manager = new LinuxDrmSyncobjManager(host.Display, host.Compositor, new DrmSyncDevice(owner._drmFd));
            WpLinuxDrmSyncobjManagerV1? proxy = null;
            var registry = host.Client.Display.GetRegistry();
            registry.Global += (_, e) =>
            {
                if (e.Interface == "wp_linux_drm_syncobj_manager_v1")
                {
                    proxy = registry.Bind<WpLinuxDrmSyncobjManagerV1>(e.Name, 1);
                }
            };
            host.PumpToClient();
            Proxy = proxy ?? throw new InvalidOperationException("no syncobj global");
        }

        public LinuxDrmSyncobjManager Manager { get; }

        public WpLinuxDrmSyncobjManagerV1 Proxy { get; }

        public (DrmSyncobjTimeline Timeline, WpLinuxDrmSyncobjTimelineV1 Proxy) Timeline()
        {
            var timeline = DrmSyncobjTimeline.Create(_owner._drmFd);
            _owner._timelines.Add(timeline);
            var fd = timeline.ExportFd();
            var proxy = Proxy.ImportTimeline(fd);
            close(fd);
            _proxies.Add(proxy);
            return (timeline, proxy);
        }

        public void Dispose()
        {
            foreach (var proxy in _proxies)
            {
                proxy.Dispose();
            }

            _host.PumpToServer();
            Manager.Dispose();
        }
    }
}
