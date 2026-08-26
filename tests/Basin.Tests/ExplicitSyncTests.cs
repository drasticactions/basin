using System.IO;
using System.Runtime.InteropServices;
using Basin.Backend.Drm;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ExplicitSyncTests : IDisposable
{
    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly int _drmFd;

    private static readonly Lazy<bool> _syncobjSupported = new(() =>
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        var fd = open(CompositorTestHost.RenderNodePath, 2);
        if (fd < 0)
        {
            return false;
        }

        try
        {
            var timeline = DrmSyncobjTimeline.TryCreate(fd);
            timeline?.Release();
            return timeline is not null;
        }
        finally
        {
            close(fd);
        }
    });

    public ExplicitSyncTests()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "explicit sync is DRM syncobjs, which are a Linux object");
        _drmFd = open(CompositorTestHost.RenderNodePath, 2);
    }

    public void Dispose()
    {
        if (_drmFd >= 0)
        {
            close(_drmFd);
        }
    }

    private void RequireDrm()
    {
        Assert.SkipWhen(_drmFd < 0, "no render node available");
        Assert.SkipWhen(!_syncobjSupported.Value, $"{CompositorTestHost.RenderNodePath} has no syncobj support");
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("skia")]
    [InlineData("gl")]
    [InlineData("skia-gl")]
    [InlineData("vulkan")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void Render_pass_fences_behave_exactly_as_declared(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        RequireDrm();

        using var host = new CompositorTestHost(renderer: renderer);
        var waitSource = DrmSyncobjTimeline.Create(_drmFd);
        waitSource.Signal(1);
        var waitFd = waitSource.ExportSyncFileAt(1);
        Assert.True(waitFd >= 0);

        var completion = DrmSyncobjTimeline.Create(_drmFd);
        var completionFd = completion.ExportFd();
        Assert.True(completionFd >= 0);

        var target = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions
        {
            WaitFenceFd = waitFd,
            SignalFenceFd = completionFd,
        });
        pass.AddRect(new RenderColor(1f, 0f, 0f, 1f), new Box(0, 0, 32, 32));
        Assert.True(pass.Submit());

        switch (host.Renderer.FencePrecision)
        {
            case RenderFencePrecision.Context:
                Assert.True(completion.Wait(0, 1_000_000_000), "a Context-precision renderer must have signaled by Submit's return");
                break;
            case RenderFencePrecision.None:
                Assert.False(completion.IsSignaled(0), "a None-precision renderer must never signal the fd");
                break;
            default:
                Assert.Fail($"unexpected declared precision {host.Renderer.FencePrecision}");
                break;
        }

        close(waitFd);
        close(completionFd);
        waitSource.Release();
        completion.Release();
        target.Destroy();
    }

    [Fact]
    public void Timeline_signal_wait_query_and_fd_round_trip()
    {
        RequireDrm();
        var timeline = DrmSyncobjTimeline.Create(_drmFd);
        Assert.False(timeline.IsSignaled(1));
        Assert.Equal(0ul, timeline.QueryLastSignaled());

        timeline.Signal(3);
        Assert.True(timeline.IsSignaled(1));
        Assert.True(timeline.IsSignaled(3));
        Assert.False(timeline.IsSignaled(4));
        Assert.Equal(3ul, timeline.QueryLastSignaled());

        var fd = timeline.ExportFd();
        Assert.True(fd >= 0);
        var imported = DrmSyncobjTimeline.ImportFd(_drmFd, fd);
        close(fd);
        Assert.True(imported.IsSignaled(3));
        timeline.Signal(5);
        Assert.True(imported.IsSignaled(5));

        var syncFile = timeline.ExportSyncFileAt(5);
        Assert.True(syncFile >= 0);
        Assert.True(RenderFences.WaitSyncFile(syncFile, 100));
        close(syncFile);

        imported.Release();
        timeline.Release();
    }

    [Fact]
    public void Sync_file_import_materialises_a_point()
    {
        RequireDrm();
        var source = DrmSyncobjTimeline.Create(_drmFd);
        source.Signal(1);
        var syncFile = source.ExportSyncFileAt(1);
        Assert.True(syncFile >= 0);

        var target = DrmSyncobjTimeline.Create(_drmFd);
        Assert.True(target.ImportSyncFileAt(7, syncFile));
        close(syncFile);
        Assert.True(target.Wait(7, 100_000_000));

        source.Release();
        target.Release();
    }

    [Fact]
    public void Protocol_round_trip_acquire_release()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var manager = new LinuxDrmSyncobjManager(host.Display, host.Compositor, new Basin.Backend.Drm.DrmSyncDevice(_drmFd));
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_linux_drm_syncobj_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var acquire = DrmSyncobjTimeline.Create(_drmFd);
        var release = DrmSyncobjTimeline.Create(_drmFd);
        var acquireFd = acquire.ExportFd();
        var releaseFd = release.ExportFd();
        var acquireProxy = proxy!.ImportTimeline(acquireFd);
        var releaseProxy = proxy.ImportTimeline(releaseFd);
        close(acquireFd);
        close(releaseFd);
        var syncSurface = proxy.GetSurface(window.Surface);
        host.PumpToServer();

        var buffer2 = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF0000FF));
        syncSurface.SetAcquirePoint(acquireProxy, 0, 1);
        syncSurface.SetReleasePoint(releaseProxy, 0, 1);
        window.Surface.Attach(buffer2.Proxy, 0, 0);
        window.Surface.Damage(0, 0, 60, 50);
        window.Surface.Commit();
        host.PumpToServer();

        Assert.Equal(1, manager.ExplicitCommits);
        Assert.Equal(1, manager.ManagedCommits);
        var sync = manager.CurrentOf(window.ServerSurface);
        Assert.NotNull(sync);
        Assert.Equal(1ul, sync!.AcquirePoint);

        Assert.False(manager.WaitAcquireCpu(window.ServerSurface, 1_000_000));

        Assert.False(manager.AcquireReady(window.ServerSurface));
        acquire.Signal(1);
        Assert.True(manager.AcquireReady(window.ServerSurface));
        Assert.True(manager.WaitAcquireCpu(window.ServerSurface, 100_000_000));

        Assert.False(release.IsSignaled(1));
        var buffer3 = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF00FF00));
        syncSurface.SetAcquirePoint(acquireProxy, 0, 2);
        syncSurface.SetReleasePoint(releaseProxy, 0, 2);
        window.Surface.Attach(buffer3.Proxy, 0, 0);
        window.Surface.Damage(0, 0, 60, 50);
        window.Surface.Commit();
        host.PumpToServer();

        Assert.Equal(2, manager.ExplicitCommits);
        Assert.True(release.Wait(1, 1_000_000_000));
        Assert.Equal(0, manager.DeferredReleases);

        acquire.Release();
        release.Release();
        syncSurface.Dispose();
        acquireProxy.Dispose();
        releaseProxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_committed_acquire_point_reaches_the_surface_as_a_fence()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var manager = new LinuxDrmSyncobjManager(host.Display, host.Compositor, new Basin.Backend.Drm.DrmSyncDevice(_drmFd));
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_linux_drm_syncobj_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var acquire = DrmSyncobjTimeline.Create(_drmFd);
        var release = DrmSyncobjTimeline.Create(_drmFd);
        var acquireFd = acquire.ExportFd();
        var releaseFd = release.ExportFd();
        var acquireProxy = proxy!.ImportTimeline(acquireFd);
        var releaseProxy = proxy.ImportTimeline(releaseFd);
        close(acquireFd);
        close(releaseFd);
        var syncSurface = proxy.GetSurface(window.Surface);
        host.PumpToServer();

        acquire.Signal(1);
        var buffer = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF0000FF));
        syncSurface.SetAcquirePoint(acquireProxy, 0, 1);
        syncSurface.SetReleasePoint(releaseProxy, 0, 1);
        window.Surface.Attach(buffer.Proxy, 0, 0);
        window.Surface.Damage(0, 0, 60, 50);
        window.Surface.Commit();
        host.PumpToServer();

        var fence = window.ServerSurface.AcquireFenceFd;
        Assert.True(fence >= 0, "a committed acquire point must reach the surface as a fence");
        Assert.True(RenderFences.WaitSyncFile(fence, 100));
        Assert.Equal(0, manager.UnexportableAcquires);

        var plain = MappedToplevel.Map(host, host.Client);
        Assert.Equal(-1, plain.ServerSurface.AcquireFenceFd);

        acquire.Release();
        release.Release();
        syncSurface.Dispose();
        acquireProxy.Dispose();
        releaseProxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_dmabuf_release_point_signals_once_the_compositor_has_stopped_reading()
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

        using var manager = new LinuxDrmSyncobjManager(host.Display, host.Compositor, new Basin.Backend.Drm.DrmSyncDevice(_drmFd));
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_linux_drm_syncobj_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var acquire = DrmSyncobjTimeline.Create(_drmFd);
        var release = DrmSyncobjTimeline.Create(_drmFd);
        var acquireFd = acquire.ExportFd();
        var releaseFd = release.ExportFd();
        var acquireProxy = proxy!.ImportTimeline(acquireFd);
        var releaseProxy = proxy.ImportTimeline(releaseFd);
        close(acquireFd);
        close(releaseFd);
        var syncSurface = proxy.GetSurface(window.Surface);
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

        acquire.Signal(1);
        syncSurface.SetAcquirePoint(acquireProxy, 0, 1);
        syncSurface.SetReleasePoint(releaseProxy, 0, 1);
        window.Surface.Attach(created, 0, 0);
        window.Surface.Damage(0, 0, 64, 64);
        window.Surface.Commit();
        host.PumpToServer();
        Assert.True(window.ServerSurface.Current.Buffer!.TryGetDmabuf(out _), "the commit must land as a real dmabuf");

        acquire.Signal(2);
        var next = host.Client.CreateBuffer(64, 64, Fill.Solid(64, 64, 0xFF00FF00));
        syncSurface.SetAcquirePoint(acquireProxy, 0, 2);
        syncSurface.SetReleasePoint(releaseProxy, 0, 2);
        window.Surface.Attach(next.Proxy, 0, 0);
        window.Surface.Damage(0, 0, 64, 64);
        window.Surface.Commit();
        host.PumpToServer();

        Assert.True(
            release.Wait(1, 1_000_000_000),
            "an idle dmabuf carries only signaled fences, so its release point must not be held back");
        Assert.Equal(0, manager.DeferredReleases);

        var outstanding = RenderFences.ExportDmabufSyncFile(planes.Fds[0], forWrite: true);
        Assert.True(outstanding >= 0, "deferring a release point needs the dmabuf sync_file ioctls");
        RenderFences.CloseFence(outstanding);

        acquire.Release();
        release.Release();
        syncSurface.Dispose();
        created!.Dispose();
        parameters.Dispose();
        acquireProxy.Dispose();
        releaseProxy.Dispose();
        host.PumpToServer();
        (allocated as BufferBase)?.Destroy();
    }

    [Fact]
    public void Two_fences_merge_into_one_that_waits_for_both()
    {
        RequireDrm();
        var first = DrmSyncobjTimeline.Create(_drmFd);
        var second = DrmSyncobjTimeline.Create(_drmFd);
        first.Signal(1);
        second.Signal(1);
        var a = first.ExportSyncFileAt(1);
        var b = second.ExportSyncFileAt(1);
        Assert.True(a >= 0 && b >= 0);

        var merged = RenderFences.MergeSyncFiles(a, b);
        Assert.True(merged >= 0);
        Assert.NotEqual(a, merged);
        Assert.NotEqual(b, merged);
        Assert.True(RenderFences.WaitSyncFile(merged, 100));

        Assert.True(RenderFences.WaitSyncFile(a, 100));
        Assert.True(RenderFences.WaitSyncFile(b, 100));
        close(merged);

        var only = RenderFences.MergeSyncFiles(a, -1);
        Assert.True(only >= 0);
        Assert.NotEqual(a, only);
        close(only);
        Assert.Equal(-1, RenderFences.MergeSyncFiles(-1, -1));

        close(a);
        close(b);
        first.Release();
        second.Release();
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("skia")]
    public void Cpu_renderers_declare_that_they_wait_on_the_cpu(string renderer)
    {
        using var host = new CompositorTestHost(renderer: renderer);
        Assert.False(host.Renderer.WaitsOnGpu);
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void Gpu_renderers_enqueue_the_wait_where_the_driver_allows(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        RequireDrm();
        using var host = new CompositorTestHost(renderer: renderer);

        Assert.True(host.Renderer.WaitsOnGpu, $"{renderer} fell back to a CPU wait on this device");
    }

    [Fact]
    public void Buffer_without_points_is_a_protocol_error()
    {
        RequireDrm();
        using var host = new CompositorTestHost();
        using var manager = new LinuxDrmSyncobjManager(host.Display, host.Compositor, new Basin.Backend.Drm.DrmSyncDevice(_drmFd));
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_linux_drm_syncobj_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpLinuxDrmSyncobjManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        _ = proxy!.GetSurface(window.Surface);
        host.PumpToServer();

        var buffer = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFFFFFFFF));
        window.Surface.Attach(buffer.Proxy, 0, 0);
        window.Surface.Commit();
        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }
}
