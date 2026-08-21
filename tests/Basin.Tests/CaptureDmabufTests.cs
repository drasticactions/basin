using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Render.Gl;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class CaptureDmabufTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc")]
    private static extern int close(int fd);

    private static bool HasRenderNode => File.Exists(CompositorTestHost.RenderNodePath);

    private static Basin.Desktop.Protocol.ZwlrScreencopyManagerV1 BindScreencopy(CompositorTestHost host)
    {
        Basin.Desktop.Protocol.ZwlrScreencopyManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_screencopy_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrScreencopyManagerV1>(e.Name, 3);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    [Fact]
    public void A_frame_offers_a_dmabuf_destination_beside_the_shm_one()
    {
        using var host = new CompositorTestHost();
        using var manager = new ScreencopyManager(
            host.Display, host.Layout, host.Buffers, new TestScreenCapture(host), TestCaptureDmabufConstraints.Typical());

        var proxy = BindScreencopy(host);
        var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        (uint Format, uint W, uint H)? shm = null;
        (uint Format, uint W, uint H)? dmabuf = null;
        var done = false;
        frame.Buffer += (_, e) => shm = ((uint)e.Format, e.Width, e.Height);
        frame.LinuxDmabuf += (_, e) => dmabuf = (e.Format, e.Width, e.Height);
        frame.BufferDone += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Equal(((uint)WlShm.Format.Xrgb8888, 160u, 120u), shm);
        Assert.Equal(((uint)DrmFormat.Xrgb8888, 160u, 120u), dmabuf);

        frame.Dispose();
        proxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Without_a_device_a_frame_offers_shm_only()
    {
        using var host = new CompositorTestHost();

        var constraints = TestCaptureDmabufConstraints.Typical();
        constraints.HasDevice = false;
        using var manager = new ScreencopyManager(
            host.Display, host.Layout, host.Buffers, new TestScreenCapture(host), constraints);

        var proxy = BindScreencopy(host);
        var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var offeredDmabuf = false;
        var announced = false;
        var done = false;
        frame.Buffer += (_, _) => announced = true;
        frame.LinuxDmabuf += (_, _) => offeredDmabuf = true;
        frame.BufferDone += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.True(announced);
        Assert.False(offeredDmabuf);

        var ready = false;
        var failed = false;
        frame.Ready += (_, _) => ready = true;
        frame.Failed += (_, _) => failed = true;
        var target = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0));
        frame.Copy(target.Proxy);
        host.PumpUntil(() => ready || failed);
        Assert.True(ready);

        frame.Dispose();
        proxy.Dispose();
        host.PumpToServer();
    }

    private static (Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1 Capture,
                    Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1 Sources)
        BindImageCopyCapture(CompositorTestHost host)
    {
        Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1? capture = null;
        Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1? sources = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "ext_image_copy_capture_manager_v1":
                    capture = registry.Bind<Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1>(e.Name, 1);
                    break;
                case "ext_output_image_capture_source_manager_v1":
                    sources = registry.Bind<Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1>(e.Name, 1);
                    break;
            }
        };
        host.PumpToClient();
        Assert.NotNull(capture);
        Assert.NotNull(sources);
        return (capture!, sources!);
    }

    private static void AssertClientAlive(CompositorTestHost host)
    {
        host.PumpToServer();
        host.PumpToClient();
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }

    [Fact]
    public void A_session_advertises_the_device_and_only_layouts_a_client_can_name()
    {
        using var host = new CompositorTestHost();
        using var sources = new ImageCaptureSourceManager(host.Display);
        using var manager = new ImageCopyCaptureManager(
            host.Display, host.Buffers, new TestScreenCapture(host), TestCaptureDmabufConstraints.Typical());

        var (captureManager, outputSources) = BindImageCopyCapture(host);
        var source = outputSources.CreateSource(host.Client.Outputs[0]);
        var session = captureManager.CreateSession(source, 0);

        byte[]? device = null;
        var formats = new List<(uint Format, byte[] Modifiers)>();
        var shmFormats = new List<uint>();
        var done = false;
        session.DmabufDevice += (_, e) => device = e.Device;
        session.DmabufFormat += (_, e) => formats.Add((e.Format, e.Modifiers));
        session.ShmFormat += (_, e) => shmFormats.Add((uint)e.Format);
        session.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.NotNull(device);
        Assert.Equal(8, device!.Length);
        Assert.Equal(TestCaptureDmabufConstraints.Device, BitConverter.ToUInt64(device));

        Assert.Equal([(uint)WlShm.Format.Xrgb8888, (uint)WlShm.Format.Argb8888], shmFormats);
        Assert.Equal(2, formats.Count);
        foreach (var (format, modifiers) in formats)
        {
            Assert.Contains(format, new[] { (uint)DrmFormat.Xrgb8888, (uint)DrmFormat.Argb8888 });
            Assert.Equal(8, modifiers.Length);
            Assert.Equal(DrmFormatSet.ModifierLinear, BitConverter.ToUInt64(modifiers));
        }

        session.Dispose();
        source.Dispose();
        captureManager.Dispose();
        outputSources.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_layout_the_frame_never_offered_fails_instead_of_reaching_the_renderer()
    {
        Assert.SkipUnless(HasRenderNode, "no render node: the dmabuf global needs a main device");
        using var host = new CompositorTestHost();

        var constraints = new TestCaptureDmabufConstraints();
        constraints.Formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        using var manager = new ScreencopyManager(
            host.Display, host.Layout, host.Buffers, new TestScreenCapture(host), constraints);

        var proxy = BindScreencopy(host);
        var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var done = false;
        frame.BufferDone += (_, _) => done = true;
        host.PumpUntil(() => done);

        const int width = 160, height = 120, stride = width * 4;
        var fd = memfd_create("basin-test-plane", 1 );
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, stride * height));

        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Add(fd, 0, 0, stride, 0, 0);
        close(fd);

        WlBuffer? created = null;
        var paramsFailed = false;
        bufferParams.Created += (_, e) => created = e.Buffer;
        bufferParams.Failed += (_, _) => paramsFailed = true;
        bufferParams.Create(width, height, (uint)DrmFormat.Argb8888, 0);
        host.PumpUntil(() => created is not null || paramsFailed);
        Assert.False(paramsFailed);

        var ready = false;
        var failed = false;
        frame.Ready += (_, _) => ready = true;
        frame.Failed += (_, _) => failed = true;
        frame.Copy(created!);
        host.PumpUntil(() => ready || failed);

        Assert.True(failed);
        Assert.False(ready);
        AssertClientAlive(host);

        created!.Dispose();
        bufferParams.Dispose();
        frame.Dispose();
        proxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_capture_is_drawn_straight_into_a_clients_dmabuf()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost(renderer: "gl");
        Assert.SkipUnless(host.Renderer is GlRenderer, "gl renderer unavailable");

        var gl = (GlRenderer)host.Renderer;
        var formats = new DrmFormatSet();
        formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        using var manager = new ScreencopyManager(
            host.Display, host.Layout, host.Buffers, new TestScreenCapture(host),
            new CaptureDmabufConstraints(formats, CompositorTestHost.RenderNodePath));

        var rect = new Basin.Scene.SceneRect(host.Scene.Root, 40, 30, new RenderColor(1, 0, 0, 1));
        rect.SetPosition(10, 20);

        using var allocator = gl.Device.CreateAllocator();
        var storage = allocator.Allocate(160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Render);
        Assert.SkipWhen(storage is null, "no linear xrgb allocation on this device");
        Assert.True(storage!.TryGetDmabuf(out var attributes));

        var proxy = BindScreencopy(host);
        var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        (uint Format, uint W, uint H)? offered = null;
        var done = false;
        frame.LinuxDmabuf += (_, e) => offered = (e.Format, e.Width, e.Height);
        frame.BufferDone += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.Equal(((uint)DrmFormat.Xrgb8888, 160u, 120u), offered);

        var bufferParams = host.Client.Dmabuf!.CreateParams();
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            bufferParams.Add(
                attributes.Fds[plane],
                (uint)plane,
                attributes.Offsets[plane],
                attributes.Strides[plane],
                (uint)(attributes.Modifier >> 32),
                (uint)attributes.Modifier);
        }

        WlBuffer? created = null;
        var paramsFailed = false;
        bufferParams.Created += (_, e) => created = e.Buffer;
        bufferParams.Failed += (_, _) => paramsFailed = true;
        bufferParams.Create(160, 120, (uint)DrmFormat.Xrgb8888, 0);
        host.PumpUntil(() => created is not null || paramsFailed);
        Assert.False(paramsFailed);

        var ready = false;
        var failed = false;
        frame.Ready += (_, _) => ready = true;
        frame.Failed += (_, _) => failed = true;
        frame.Copy(created!);
        host.PumpUntil(() => ready || failed);
        Assert.True(ready);

        var readback = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        using (var texture = gl.ImportTexture(storage))
        {
            Assert.NotNull(texture);
            var pass = gl.BeginBufferPass(readback, new RenderPassOptions());
            pass.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(0, 0, 160, 120) });
            Assert.True(pass.Submit());
        }

        Assert.True(readback.BeginDataAccess(BufferDataAccess.Read, out var view));
        unsafe
        {
            var inside = *(uint*)(view.Data + 25 * view.Stride + 15 * 4) | 0xFF000000u;
            var outside = *(uint*)(view.Data + 100 * view.Stride + 100 * 4) | 0xFF000000u;
            Assert.Equal(0xFFFF0000u, inside);
            Assert.Equal(0xFF000000u, outside);
        }

        readback.EndDataAccess();
        readback.Destroy();

        created!.Dispose();
        bufferParams.Dispose();
        frame.Dispose();
        proxy.Dispose();
        host.PumpToServer();
        ((BufferBase)storage).Destroy();
    }
}
