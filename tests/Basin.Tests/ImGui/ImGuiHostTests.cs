using System.Numerics;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.UI.ImGui;
using Hexa.NET.ImGui;
using Hexa.NET.ImNodes;
using Hexa.NET.ImPlot;
using Xunit;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Tests;

public sealed class ImGuiHostTests
{
    public static TheoryData<string, string> Rows => new()
    {
        { "gl", "gl" },
        { "skia-gl", "gl" },
        { "vulkan", "gl" },
        { "vulkan", "vulkan" },
        { "skia-vulkan", "vulkan" },
        { "skia-graphite", "vulkan" },
        { "gl", "vulkan" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_drawn_rect_lands_premultiplied_in_the_chrome_buffer(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = ImGuiLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"{row} builds no {backend} imgui host");
        Assert.Equal(ImGuiLease.SharesDevice(host.Renderer, backend), !lease!.OwnsDevice);

        var surface = lease.Create(64, 32);
        var green = 0x40;
        surface.Content = new Rect(() => new Vector4(200 / 255f, green / 255f, 50 / 255f, 128 / 255f));
        for (var pass = 0; pass < 3; pass++)
        {
            green = 0x40 + (pass * 0x20);
            surface.Invalidate();
            Assert.True(surface.Draw());
            Assert.True(surface.TryAcquire(out var frame));
            var reader = lease.Reader(host);
            Assert.True(BufferCapture.TryReadRgba(frame.Buffer!, reader, out var rgba), $"{row} did not import the chrome buffer");
            var i = ((16 * 64) + 32) * 4;
            Assert.InRange(rgba![i], 100 - 2, 100 + 2);
            Assert.InRange(rgba[i + 1], (green / 2) - 2, (green / 2) + 2);
            Assert.InRange(rgba[i + 2], 25 - 2, 25 + 2);
            Assert.InRange(rgba[i + 3], 128 - 1, 128 + 1);
            Assert.Equal(0, rgba[3]);
            frame.Dispose();
        }

        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_drawn_frame_publishes_a_write_fence_rather_than_finishing(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = ImGuiLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"{row} builds no {backend} imgui host");
        var probe = lease!.ProbeFence(out var devicePath);
        Assert.SkipWhen(probe < 0, $"{devicePath} exports no native fence, so every frame waits for the GPU");
        RenderFences.CloseFence(probe);

        var surface = lease.Create(120, 40);
        var pass = 0;
        surface.Content = new Rect(() => new Vector4(pass / 4f, 0.5f, 0.5f, 1f));
        for (pass = 0; pass < 4; pass++)
        {
            surface.Invalidate();
            surface.Draw();
            Assert.True(surface.TryAcquire(out var frame));
            Assert.True(frame.Buffer!.TryGetDmabuf(out var attributes));
            var fence = RenderFences.ExportDmabufSyncFile(attributes.Fds[0], forWrite: false);
            Assert.True(fence >= 0, "the chrome dmabuf carries no write fence");
            Assert.True(RenderFences.WaitSyncFile(fence), "the chrome's write fence never signalled");
            RenderFences.CloseFence(fence);
            frame.Dispose();
        }

        Assert.Equal(4, lease.FencedFrames);
        Assert.Equal(0, lease.FinishedFrames);
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_surface_redrawn_and_resized_retires_its_buffers_and_tears_down_clean(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = ImGuiLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"{row} builds no {backend} imgui host");

        var surface = lease!.Create(200, 60);
        var label = "one";
        surface.Content = new Text(() => label);
        surface.Draw();
        Assert.True(surface.TryAcquire(out var held));

        Assert.True(surface.Configure(240, 72, 1.0));
        label = "two";
        surface.Draw();
        Assert.True(surface.TryAcquire(out var second));
        second.Dispose();
        held.Dispose();

        Assert.True(surface.Configure(200, 60, 1.5));
        label = "three";
        surface.Draw();
        Assert.True(surface.TryAcquire(out var third));
        Assert.Equal(300, third.Buffer!.Width);
        third.Dispose();
        Assert.True(lease.LiveTextures >= 1);
        surface.Dispose();
        lease.Dispose();
        Assert.Equal(0, lease.LiveTextures);
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("skia")]
    public void A_renderer_that_imports_no_dmabuf_is_declined_by_name(string row)
    {
        using var host = new CompositorTestHost(renderer: row);
        var imgui = ImGuiGlUIHost.TryCreate(
            ImGuiLease.DeviceOf(host.Renderer), CompositorTestHost.RenderNodePath, host.Renderer.DmabufTextureFormats,
            out var declined);

        Assert.Null(imgui);
        Assert.Contains("imports no", declined, StringComparison.Ordinal);
    }

    [Fact]
    public void No_shared_device_and_no_render_node_is_declined_by_name()
    {
        var imgui = ImGuiGlUIHost.TryCreate(null, null, null, out var declined);

        Assert.Null(imgui);
        Assert.Contains("render node", declined, StringComparison.Ordinal);
    }

    [Fact]
    public void Golden_imgui_demo() => AssertDemoGolden("gl", "gl", "imgui-demo-gl");

    [Fact]
    public void Golden_imgui_demo_on_vulkan() => AssertDemoGolden("vulkan", "vulkan", "imgui-demo-vk");

    [Theory]
    [InlineData("gl", "gl")]
    [InlineData("skia-gl", "gl")]
    [InlineData("vulkan", "vulkan")]
    [InlineData("skia-vulkan", "vulkan")]
    public void The_host_follows_the_renderer_device(string row, string expected)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var imgui = Basin.Renderers.ImGuiHosts.For(
            host.Renderer, null, CompositorTestHost.RenderNodePath, out var declined, out var vulkanDeclined);

        Assert.True(imgui is not null, $"{row}: {declined} / {vulkanDeclined}");
        Assert.Equal(expected, imgui!.Name);
    }

    [Theory]
    [InlineData("pixman")]
    [InlineData("skia")]
    public void Both_hosts_decline_a_renderer_that_imports_no_dmabuf(string row)
    {
        using var host = new CompositorTestHost(renderer: row);
        var imgui = Basin.Renderers.ImGuiHosts.For(
            host.Renderer, null, CompositorTestHost.RenderNodePath, out var declined, out _);
        var vulkan = ImGuiVulkanUIHost.TryCreate(
            null, CompositorTestHost.RenderNodePath, host.Renderer.DmabufTextureFormats, out var vulkanDeclined);

        Assert.Null(imgui);
        Assert.Null(vulkan);
        Assert.Contains("imports no", declined, StringComparison.Ordinal);
        Assert.Contains("imports no", vulkanDeclined, StringComparison.Ordinal);
    }

    internal static void AssertDemoGolden(string row, string backend, string golden)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipWhen(!CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this driver");
        using var host = new CompositorTestHost(renderer: row);
        using var lease = ImGuiLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"this row builds no {backend} imgui host");

        var surface = lease!.Create(640, 480);
        using (surface.Contexts.Enter())
        {
            ImGuiApi.GetIO().ConfigInputTextCursorBlink = false;
        }

        surface.Content = new Demo();
        for (var i = 0; i < 4; i++)
        {
            lease.Clock.Advance(16);
            surface.Invalidate();
            surface.Draw();
        }

        Assert.True(surface.TryAcquire(out var frame));
        var buffer = frame.Buffer!;
        Assert.True(BufferCapture.TryReadRgba(buffer, lease.Reader(host), out var rgba));
        Golden.AssertMatches(rgba!, buffer.Width, buffer.Height, golden, tolerance: 6);
        frame.Dispose();
        surface.Dispose();
    }

    internal sealed class Demo : IImGuiContent
    {
        public void Build(ImGuiFrame frame)
        {
            ImGuiApi.SetWindowPos("Dear ImGui Demo", new Vector2(10, 10), ImGuiCond.Always);
            ImGuiApi.SetWindowSize("Dear ImGui Demo", new Vector2(330, 460), ImGuiCond.Always);
            ImGuiApi.ShowDemoWindow();

            ImGuiApi.SetNextWindowPos(new Vector2(350, 10), ImGuiCond.Always);
            ImGuiApi.SetNextWindowSize(new Vector2(280, 200), ImGuiCond.Always);
            ImGuiApi.Begin("plot");
            if (ImPlot.BeginPlot("frames", new Vector2(-1, -1)))
            {
                ImPlot.PlotLine("ms", ref Samples.Values[0], Samples.Values.Length);
                ImPlot.EndPlot();
            }

            ImGuiApi.End();

            ImGuiApi.SetNextWindowPos(new Vector2(350, 220), ImGuiCond.Always);
            ImGuiApi.SetNextWindowSize(new Vector2(280, 250), ImGuiCond.Always);
            ImGuiApi.Begin("nodes");
            ImNodes.BeginNodeEditor();
            Node(1, "source", new Vector2(10, 10), output: 2);
            Node(3, "effect", new Vector2(130, 60), output: 4);
            Node(5, "post", new Vector2(40, 130), output: 6);
            ImNodes.Link(7, 2, 103);
            ImNodes.Link(8, 4, 105);
            ImNodes.EndNodeEditor();
            ImGuiApi.End();
        }

        private static void Node(int id, string title, Vector2 position, int output)
        {
            ImNodes.SetNodeGridSpacePos(id, position);
            ImNodes.BeginNode(id);
            ImNodes.BeginNodeTitleBar();
            ImGuiApi.TextUnformatted(title);
            ImNodes.EndNodeTitleBar();
            ImNodes.BeginInputAttribute(id + 100);
            ImGuiApi.TextUnformatted("in");
            ImNodes.EndInputAttribute();
            ImNodes.BeginOutputAttribute(output);
            ImGuiApi.TextUnformatted("out");
            ImNodes.EndOutputAttribute();
            ImNodes.EndNode();
        }
    }

    private static class Samples
    {
        public static readonly float[] Values = Build();

        private static float[] Build()
        {
            var values = new float[60];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = 16f + (4f * MathF.Sin(i / 6f));
            }

            return values;
        }
    }

    private sealed class Rect(Func<Vector4> color) : IImGuiContent
    {
        public void Build(ImGuiFrame frame)
        {
            var c = color();
            ImGuiApi.GetBackgroundDrawList().AddRectFilled(
                new Vector2(8, 4), new Vector2(frame.Width - 8, frame.Height - 4),
                ImGuiApi.ColorConvertFloat4ToU32(c));
        }
    }

    private sealed class Text(Func<string> label) : IImGuiContent
    {
        public void Build(ImGuiFrame frame)
        {
            ImGuiApi.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
            ImGuiApi.Begin("text");
            ImGuiApi.TextUnformatted(label());
            ImGuiApi.End();
        }
    }

    internal sealed class ImGuiLease : IDisposable
    {
        private bool _disposed;

        private ImGuiLease(ImGuiUIHost host)
        {
            Host = host;
        }

        public ImGuiUIHost Host { get; }

        public PaperSurfaceTests.FakeClock Clock { get; } = new();

        public bool OwnsDevice => Gl?.OwnsDevice ?? Vulkan!.OwnsDevice;

        public int FencedFrames => Gl?.FencedFrames ?? Vulkan!.FencedFrames;

        public int FinishedFrames => Gl?.FinishedFrames ?? Vulkan!.FinishedFrames;

        public int LiveTextures => Gl?.LiveTextures ?? Vulkan?.Textures.Live ?? 0;

        internal ImGuiGlBackend? Gl => Host.Backend as ImGuiGlBackend;

        internal ImGuiVulkanBackend? Vulkan => Host.Backend as ImGuiVulkanBackend;

        public IRenderer Reader(CompositorTestHost host) => host.Renderer;

        public ImGuiUISurface Create(int width, int height, double scale = 1.0) =>
            Host.Create(new UISurfaceOptions
            {
                Target = UITargetKind.Dmabuf,
                Width = width,
                Height = height,
                Scale = scale,
            }) ?? throw new InvalidOperationException("the imgui host made no surface");

        public int ProbeFence(out string devicePath)
        {
            if (Gl is { } gl)
            {
                devicePath = gl.Device.DevicePath;
                using (gl.Context.Enter())
                {
                    return gl.Device.ExportFence();
                }
            }

            devicePath = Vulkan!.Device.DevicePath;
            return Vulkan.Device.ExportQueueFence();
        }

        public static ImGuiLease? Open(CompositorTestHost host, string backend)
        {
            var clock = new PaperSurfaceTests.FakeClock();
            var options = new ImGuiHostOptions { ClockNanos = clock.Now };
            var node = host.Renderer.Device?.DevicePath ?? CompositorTestHost.RenderNodePath;
            var imgui = backend == "vulkan"
                ? ImGuiVulkanUIHost.TryCreate(
                    host.Renderer.Device as Basin.Render.Vulkan.VulkanDevice, node, host.Renderer.DmabufTextureFormats,
                    out _, options: options)
                : ImGuiGlUIHost.TryCreate(
                    DeviceOf(host.Renderer), node, host.Renderer.DmabufTextureFormats, out _, options: options);
            return imgui is null ? null : new ImGuiLease(imgui, clock);
        }

        private ImGuiLease(ImGuiUIHost host, PaperSurfaceTests.FakeClock clock)
            : this(host)
        {
            Clock = clock;
        }

        public static GlDevice? DeviceOf(IRenderer renderer) => renderer switch
        {
            GlRenderer gl => gl.Device,
            Basin.Render.Skia.SkiaGlRenderer skia => skia.Device,
            _ => null,
        };

        public static bool SharesDevice(IRenderer renderer, string backend) =>
            backend == "vulkan" ? renderer.Device is Basin.Render.Vulkan.VulkanDevice : DeviceOf(renderer) is not null;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Host.Dispose();
            }
        }
    }
}
