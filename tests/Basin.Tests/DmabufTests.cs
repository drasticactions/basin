using System.Runtime.InteropServices;
using Basin.Protocol;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class DmabufTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc")]
    private static extern int munmap(nint addr, nuint length);

    private static bool HasRenderNode => File.Exists(CompositorTestHost.RenderNodePath);

    private static int CreatePlaneFd(int size)
    {
        var fd = memfd_create("basin-test-plane", 1 );
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, size));
        return fd;
    }

    [Fact]
    public void Feedback_carries_main_device_and_format_table()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        var feedback = host.Client.Dmabuf!.GetDefaultFeedback();

        var done = false;
        byte[]? mainDevice = null;
        byte[]? trancheDevice = null;
        byte[]? indices = null;
        int tableFd = -1;
        uint tableSize = 0;
        feedback.FormatTable += (_, e) => (tableFd, tableSize) = (e.Fd, e.Size);
#pragma warning disable CS0618
        feedback.MainDevice += (_, e) => mainDevice = e.Device;
#pragma warning restore CS0618
        feedback.TrancheTargetDevice += (_, e) => trancheDevice = e.Device;
        feedback.TrancheFormats += (_, e) => indices = e.Indices;
        feedback.Done += (_, _) => done = true;

        host.PumpUntil(() => done);

        Assert.NotNull(mainDevice);
        Assert.Equal(8, mainDevice!.Length);
        Assert.Equal(mainDevice, trancheDevice);

        Assert.True(tableFd >= 0);
        Assert.Equal(0u, tableSize % 16);
        var entryCount = (int)(tableSize / 16);
        Assert.Equal(entryCount * 2, indices!.Length);

        var map = mmap(0, tableSize, 1 , 1 , tableFd, 0);
        Assert.NotEqual(-1, (long)map);
        var seen = new List<(uint Fourcc, ulong Modifier)>();
        unsafe
        {
            for (var i = 0; i < entryCount; i++)
            {
                var fourcc = *(uint*)(map + i * 16);
                var modifier = *(ulong*)(map + i * 16 + 8);
                seen.Add((fourcc, modifier));
            }
        }

        munmap(map, tableSize);
        close(tableFd);

        Assert.Contains(((uint)DrmFormat.Argb8888, DrmFormatSet.ModifierLinear), seen);
        Assert.Contains(((uint)DrmFormat.Xrgb8888, (ulong)DrmFormatSet.ModifierInvalid), seen);
    }

    [Fact]
    public void Valid_params_create_a_buffer_that_attaches()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        const int width = 8, height = 8, stride = width * 4;
        var fd = CreatePlaneFd(stride * height);

        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Add(fd, 0, 0, stride, 0, 0);
        close(fd);

        WlBuffer? created = null;
        var failed = false;
        bufferParams.Created += (_, e) => created = e.Buffer;
        bufferParams.Failed += (_, _) => failed = true;
        bufferParams.Create(width, height, (uint)DrmFormat.Argb8888, 0);
        host.PumpUntil(() => created is not null || failed);
        Assert.False(failed);

        var surface = host.Client.Compositor.CreateSurface();
        surface.Attach(created, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var serverBuffer = host.SurfaceScenes[0].Surface.Current.Buffer;
        Assert.IsType<DmabufBuffer>(serverBuffer);
        Assert.Equal((width, height), (serverBuffer!.Width, serverBuffer.Height));
        Assert.True(((DmabufBuffer)serverBuffer).TryGetDmabuf(out var attributes));
        Assert.Equal(DrmFormat.Argb8888, attributes.Format);
        Assert.Equal(DrmFormatSet.ModifierLinear, attributes.Modifier);

        surface.Attach(null, 0, 0);
        surface.Commit();
        created!.Dispose();
        bufferParams.Dispose();
        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_destroyed_buffer_still_on_screen_keeps_its_planes()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        const int width = 8, height = 8, stride = width * 4;
        var fd = CreatePlaneFd(stride * height);

        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Add(fd, 0, 0, stride, 0, 0);
        close(fd);

        WlBuffer? created = null;
        var failed = false;
        bufferParams.Created += (_, e) => created = e.Buffer;
        bufferParams.Failed += (_, _) => failed = true;
        bufferParams.Create(width, height, (uint)DrmFormat.Argb8888, 0);
        host.PumpUntil(() => created is not null || failed);
        Assert.False(failed);

        var surface = host.Client.Compositor.CreateSurface();
        surface.Attach(created, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var serverBuffer = (DmabufBuffer)host.SurfaceScenes[0].Surface.Current.Buffer!;

        created!.Dispose();
        host.PumpToServer();
        Assert.True(serverBuffer.IsDestroyed);
        Assert.True(serverBuffer.TryGetDmabuf(out var attributes));
        Assert.Equal(DrmFormat.Argb8888, attributes.Format);

        surface.Attach(null, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.False(serverBuffer.TryGetDmabuf(out _));

        bufferParams.Dispose();
        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Unsupported_format_fails_cleanly()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        const int stride = 8 * 4;
        var fd = CreatePlaneFd(stride * 8);

        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Add(fd, 0, 0, stride, 0, 0);
        close(fd);

        var failed = false;
        var created = false;
        bufferParams.Created += (_, _) => created = true;
        bufferParams.Failed += (_, _) => failed = true;
        bufferParams.Create(8, 8, 0x3231564E , 0);
        host.PumpUntil(() => failed || created);

        Assert.True(failed);
        Assert.False(created);
        bufferParams.Dispose();
        host.PumpToClient();
    }

    [Fact]
    public void Plane_exceeding_its_fd_fails_cleanly()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        const int stride = 8 * 4;
        var fd = CreatePlaneFd(stride * 4);

        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Add(fd, 0, 0, stride, 0, 0);
        close(fd);

        var failed = false;
        bufferParams.Failed += (_, _) => failed = true;
        bufferParams.Create(8, 8, (uint)DrmFormat.Argb8888, 0);
        host.PumpUntil(() => failed);
    }

    [Fact]
    public void Out_of_range_plane_index_is_a_protocol_error()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        var fd = CreatePlaneFd(64);

        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Add(fd, 7, 0, 16, 0, 0);
        close(fd);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Fact]
    public void Create_without_planes_is_a_protocol_error()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Create(8, 8, (uint)DrmFormat.Argb8888, 0);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Fact]
    public void Ten_thousand_imports_leak_nothing()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        const int width = 4, height = 4, stride = width * 4;
        var fd = CreatePlaneFd(stride * height);

        for (var i = 0; i < 10_000; i++)
        {
            var bufferParams = host.Client.Dmabuf!.CreateParams();
            bufferParams.Add(fd, 0, 0, stride, 0, 0);
            var buffer = bufferParams.CreateImmed(width, height, (uint)DrmFormat.Argb8888, 0);
            buffer.Dispose();
            bufferParams.Dispose();
            if (i % 16 == 0)
            {
                host.PumpToClient();
            }
        }

        close(fd);
        host.PumpUntil(() => host.Buffers.Count == 0, rounds: 2000);
    }

    [Fact]
    public void Unsupported_format_via_create_immed_is_a_protocol_error()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        const int stride = 8 * 4;
        var fd = CreatePlaneFd(stride * 8);

        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Add(fd, 0, 0, stride, 0, 0);
        close(fd);
        bufferParams.CreateImmed(8, 8, 0x3231564E , 0);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    [InlineData("impeller")]
    public void Dmabuf_clients_render_correctly_across_modifiers(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        Assert.SkipUnless(HasRenderNode, "no render node");

        using var host = new CompositorTestHost(renderer: renderer);
        var fillRenderer = renderer is "gl" or "impeller" ? host.Renderer : new Basin.Render.Gl.GlRenderer(CompositorTestHost.RenderNodePath);
        using var fillGuard = ReferenceEquals(fillRenderer, host.Renderer) ? null : (Basin.Render.Gl.GlRenderer)fillRenderer;
        using var allocator = (fillRenderer switch
        {
            Basin.Render.Gl.GlRenderer gl => gl.Device,
            Basin.Render.Impeller.ImpellerGlRenderer impeller => impeller.Device,
            _ => throw new InvalidOperationException("unreachable: the fill renderer is always GL-backed"),
        }).CreateAllocator();

        var advertised = host.Renderer.DmabufTextureFormats;
        var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Xrgb8888)
            .Where(m => m != DrmFormatSet.ModifierInvalid && advertised.Contains(DrmFormat.Xrgb8888, m))
            .ToArray();
        Assert.SkipWhen(modifiers.Length < 3, $"the device offers only {modifiers.Length} xrgb modifiers");
        var colors = new uint[] { 0xFFE04010, 0xFF10E040, 0xFF1040E0, 0xFFE0E010, 0xFFE010E0, 0xFF10E0E0, 0xFFA0A0A0, 0xFF804020 };
        var verified = new List<ulong>();

        foreach (var (modifier, index) in modifiers.Select((m, i) => (m, i)))
        {
            var color = colors[index % colors.Length];
            var source = allocator.Allocate(32, 32, DrmFormat.Xrgb8888, [modifier], BufferUse.Render);
            if (source is null)
            {
                continue;
            }

            var pass = fillRenderer.BeginBufferPass(source, new RenderPassOptions());
            pass.AddRect(
                new RenderColor(((color >> 16) & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, (color & 0xFF) / 255f, 1f),
                new Box(0, 0, 32, 32));
            Assert.True(pass.Submit());

            if (!SurvivesItsOwnRenderer(fillRenderer, source, color))
            {
                (source as BufferBase)!.Destroy();
                continue;
            }

            Assert.True(source.TryGetDmabuf(out var attributes));
            Assert.Equal(modifier, attributes.Modifier);

            var bufferParams = host.Client.Dmabuf!.CreateParams();
            for (var plane = 0; plane < attributes.PlaneCount; plane++)
            {
                bufferParams.Add(
                    attributes.Fds[plane],
                    (uint)plane,
                    attributes.Offsets[plane],
                    attributes.Strides[plane],
                    (uint)(modifier >> 32),
                    (uint)modifier);
            }

            var wlBuffer = bufferParams.CreateImmed(32, 32, (uint)DrmFormat.Xrgb8888, 0);
            bufferParams.Dispose();

            var surface = host.Client.Compositor.CreateSurface();
            surface.Attach(wlBuffer, 0, 0);
            surface.Commit();
            host.PumpToServer();

            try
            {
                host.RenderFrame();
                Assert.Equal(color, host.Pixel(2, 2));
                Assert.Equal(color, host.Pixel(29, 29));
            }
            finally
            {
                surface.Dispose();
                wlBuffer.Dispose();
                (source as BufferBase)!.Destroy();
                host.PumpToClient();
            }

            verified.Add(modifier);
        }

        Assert.True(verified.Count >= 3, $"only {verified.Count} modifiers verified: {string.Join(", ", verified.Select(m => $"0x{m:X}"))}");
    }

    private static bool SurvivesItsOwnRenderer(IRenderer renderer, IBuffer filled, uint color)
    {
        using var texture = renderer.ImportTexture(filled);
        if (texture is null)
        {
            return false;
        }

        var target = new MemoryBuffer(filled.Width, filled.Height, DrmFormat.Xrgb8888);
        try
        {
            var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddTexture(texture, new TextureRenderOptions
            {
                DstBox = new Box(0, 0, filled.Width, filled.Height),
            });
            if (!pass.Submit() || !target.BeginDataAccess(BufferDataAccess.Read, out var view))
            {
                return false;
            }

            try
            {
                unsafe
                {
                    var near = *(uint*)((byte*)view.Data + (2 * view.Stride) + (2 * 4));
                    var far = *(uint*)((byte*)view.Data
                        + ((filled.Height - 3) * view.Stride) + ((filled.Width - 3) * 4));
                    return near == color && far == color;
                }
            }
            finally
            {
                target.EndDataAccess();
            }
        }
        finally
        {
            target.Destroy();
        }
    }
}

public sealed class DmabufVersionTests
{
    [DllImport("libc")]
    private static extern int close(int fd);

    private static bool HasRenderNode => File.Exists(CompositorTestHost.RenderNodePath);

    [Theory]
    [InlineData(5u, true)]
    [InlineData((uint)LinuxDmabufGlobal.Version, false)]
    public void Main_device_is_sent_below_six_only(uint version, bool expectMainDevice)
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        var dmabuf = Bind(host, version);

        var feedback = dmabuf.GetDefaultFeedback();
        var mainDevices = 0;
        var trancheFlags = new List<uint>();
        var done = false;
        feedback.FormatTable += (_, e) => close(e.Fd);
#pragma warning disable CS0618
        feedback.MainDevice += (_, _) => mainDevices++;
#pragma warning restore CS0618
        feedback.TrancheFlagsEvent += (_, e) => trancheFlags.Add((uint)e.Flags);
        feedback.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Equal(expectMainDevice ? 1 : 0, mainDevices);

        Assert.NotEmpty(trancheFlags);
        Assert.All(trancheFlags, flags => Assert.NotEqual(0u, flags));
        Assert.Contains(trancheFlags, flags => (flags & 2) != 0);
    }

    [Fact]
    public void Planes_disagreeing_on_the_modifier_are_an_error()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        var fd = memfd_create("basin-test-plane", 1 );
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, 1024));

        var bufferParams = Bind(host, LinuxDmabufGlobal.Version).CreateParams();
        bufferParams.Add(fd, 0, 0, 32, 0, 0);
        bufferParams.Add(fd, 1, 0, 32, 0, 1);
        close(fd);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Fact]
    public void A_sampling_device_that_is_not_a_dev_t_is_an_error()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        var bufferParams = Bind(host, LinuxDmabufGlobal.Version).CreateParams();

        bufferParams.SetSamplingDevice([1, 2, 3, 4]);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Fact]
    public void A_named_sampling_device_reaches_the_buffer()
    {
        Assert.SkipUnless(HasRenderNode, "no render node");
        using var host = new CompositorTestHost();
        Assert.True(DrmDevices.TryDeviceId(CompositorTestHost.RenderNodePath, out var deviceId));

        const int width = 8, height = 8, stride = width * 4;
        var fd = memfd_create("basin-test-plane", 1 );
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, stride * height));

        var bufferParams = Bind(host, LinuxDmabufGlobal.Version).CreateParams();
        bufferParams.SetSamplingDevice(BitConverter.GetBytes(deviceId));
        bufferParams.Add(fd, 0, 0, stride, 0, 0);
        close(fd);

        WlBuffer? created = null;
        bufferParams.Created += (_, e) => created = e.Buffer;
        bufferParams.Create(width, height, (uint)DrmFormat.Argb8888, 0);
        host.PumpUntil(() => created is not null);

        var surface = host.Client.Compositor.CreateSurface();
        surface.Attach(created, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var buffer = Assert.IsType<DmabufBuffer>(host.SurfaceScenes[0].Surface.Current.Buffer);
        Assert.Equal(deviceId, buffer.SamplingDevice);

        surface.Attach(null, 0, 0);
        surface.Commit();
        created!.Dispose();
        surface.Dispose();
        host.PumpToServer();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    private static Basin.Protocol.ZwpLinuxDmabufV1 Bind(CompositorTestHost host, uint version)
    {
        Basin.Protocol.ZwpLinuxDmabufV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_linux_dmabuf_v1")
            {
                proxy = registry.Bind<Basin.Protocol.ZwpLinuxDmabufV1>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}
