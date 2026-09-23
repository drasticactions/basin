using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.Scene;
using Basin.UI.Quill;
using Pixman;
using Prowl.Quill;
using Prowl.Vector;
using Xunit;

namespace Basin.Tests;

public sealed class QuillHostTests
{
    public static TheoryData<string, string> Rows => new()
    {
        { "gl", "gl" },
        { "skia-gl", "gl" },
        { "vulkan", "gl" },
        { "skia-vulkan", "gl" },
        { "skia-graphite", "gl" },
        { "impeller", "gl" },
        { "vulkan", "vulkan" },
        { "skia-vulkan", "vulkan" },
        { "skia-graphite", "vulkan" },
        { "gl", "vulkan" },
    };

    [Fact]
    public void FrameHosts_matches_a_renderer_to_the_host_that_serves_its_contract()
    {
        var skia = new ContractHost(typeof(FakeSkiaSurface));
        var quill = new ContractHost(typeof(IQuillUISurface));
        var hosts = new FrameHosts(skia, quill);

        Assert.Same(skia, hosts.For(new ContractRenderer(typeof(FakeSkiaSurface))));
        Assert.Same(quill, hosts.For(new ContractRenderer(typeof(IQuillUISurface))));
        Assert.Same(skia, hosts.For(new ContractRenderer(typeof(IUISurface))));
    }

    [Fact]
    public void A_renderer_no_host_serves_resolves_to_nothing_and_is_named()
    {
        var hosts = new FrameHosts(new ContractHost(typeof(FakeSkiaSurface)));
        var renderer = new ContractRenderer(typeof(IQuillUISurface));

        Assert.Null(hosts.For(renderer));
        Assert.Contains(nameof(IQuillUISurface), hosts.Describe(renderer), StringComparison.Ordinal);
        Assert.Contains(nameof(FakeSkiaSurface), hosts.Describe(renderer), StringComparison.Ordinal);
    }

    [Fact]
    public void A_frame_whose_renderer_no_host_serves_faults_rather_than_casting()
    {
        using var host = new CompositorTestHost();
        var scene = new Scene.Scene();
        var hosts = new FrameHosts(new ContractHost(typeof(FakeSkiaSurface)));
        var frame = new Frame(hosts, new ContractRenderer(typeof(IQuillUISurface)), scene.Root);
        Exception? fault = null;
        frame.Faulted += e => fault = e;

        frame.Configure(new Box(0, 0, 100, 80), 1.0, new FrameState { Title = "x" });

        Assert.True(frame.IsFaulted);
        Assert.IsType<InvalidOperationException>(fault);
        Assert.Contains(nameof(IQuillUISurface), fault!.Message, StringComparison.Ordinal);
        frame.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_frame_puts_its_backdrop_on_the_strips_that_carry_it()
    {
        using var testHost = new CompositorTestHost();
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var renderer = new CountingFrameRenderer { Backdrop = new Box(0, 0, 128, 34) };
        var effect = new ProbeBackdropEffect();
        var frame = new Frame(host, renderer, scene.Root) { BackdropEffect = effect };
        var state = new FrameState { Title = "frost", Active = true };

        frame.Configure(new Box(0, 0, 120, 90), 1.0, state);
        frame.Commit();

        var top = frame.StripNodes[0];
        Assert.True(top.HasActiveBackdrop);
        Assert.Same(effect, top.BackdropEffect);
        var extents = top.BackdropRegion!.Extents;
        Assert.Equal((0, 0, 128, 30), (extents.X1, extents.Y1, extents.X2, extents.Y2));
        Assert.True(frame.StripNodes[1].HasActiveBackdrop, "the region's last four rows reach the left border");
        Assert.True(frame.StripNodes[2].HasActiveBackdrop, "and the right border");
        Assert.False(frame.StripNodes[3].HasActiveBackdrop, "but not the bottom border");

        var left = frame.StripNodes[1].BackdropRegion!.Extents;
        Assert.Equal((0, 0, 4, 4), (left.X1, left.Y1, left.X2, left.Y2));

        frame.BackdropEffect = null;
        frame.Configure(new Box(0, 0, 120, 92), 1.0, state);
        frame.Commit();
        Assert.False(frame.StripNodes[0].HasActiveBackdrop);

        frame.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_frosted_renderer_asks_for_a_backdrop_shaped_like_its_whole_chrome()
    {
        var flat = new Basin.Frames.Quill.QuillFrameRenderer(
            new Basin.Frames.Quill.QuillFrameTheme { Face = Basin.Frames.Quill.QuillFrameFonts.Bundled() });
        var frosted = new Basin.Frames.Quill.QuillFrameRenderer(
            new Basin.Frames.Quill.QuillFrameTheme
            {
                Face = Basin.Frames.Quill.QuillFrameFonts.Bundled(),
                CornerRadius = 8f,
                Frosted = true,
            });
        var state = new FrameState { Title = "frost", Active = true };
        using var region = new PixmanRegion32();

        Assert.False(flat.BackdropRegion(state, 1.0, region));
        Assert.False(frosted.BackdropRegion(state, 1.0, region));

        Draw(flat, state);
        Draw(frosted, state);

        Assert.False(flat.BackdropRegion(state, 1.0, region));
        Assert.True(frosted.BackdropRegion(state, 1.0, region));

        var geometry = frosted.Geometry;
        var extents = region.Extents;
        Assert.Equal(0, extents.X1);
        Assert.Equal(0, extents.Y1);
        Assert.Equal(geometry.Width, extents.X2);
        Assert.Equal(geometry.Height, extents.Y2);
        Assert.False(Covers(region, 0, 0), "the top-left corner is outside the blurred region");
        Assert.False(Covers(region, 0, geometry.Height - 1), "and so is the bottom-left corner");
        Assert.True(Covers(region, geometry.Width / 2, 1), "the titlebar's top edge is inside it");
        Assert.True(Covers(region, 1, geometry.Height / 2), "the side border is inside it");
        Assert.True(
            Covers(region, geometry.Width / 2, geometry.Height - 1),
            "and so is the bottom border, or the frame would frost in two materials");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_surface_redrawn_and_resized_retires_its_buffers_and_tears_down_clean(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillLease.Require(host, backend);

        var surface = (IQuillUISurface)lease.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 200,
            Height = 60,
            Scale = 1.0,
        })!;

        Paint(surface, "one");
        Assert.True(surface.TryAcquire(out var held));

        Assert.True(surface.Configure(240, 72, 1.0));
        Paint(surface, "two");
        Assert.True(surface.TryAcquire(out var second));
        second.Dispose();

        held.Dispose();

        Assert.True(surface.Configure(200, 60, 1.0));
        Paint(surface, "three");
        Assert.True(surface.TryAcquire(out var third));
        third.Dispose();
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Enough_glyphs_to_grow_the_atlas_leave_no_texture_behind(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillLease.Require(host, backend);

        var surface = (IQuillUISurface)lease.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 400,
            Height = 200,
            Scale = 1.0,
        })!;

        var face = Basin.Frames.Quill.QuillFrameFonts.Bundled();
        for (var pass = 0; pass < 4; pass++)
        {
            var canvas = surface.BeginDraw();
            for (var line = 0; line < 12; line++)
            {
                canvas.DrawText(
                    $"{(char)('A' + line)}{pass} sample glyph run {line} 0123456789",
                    4f, 8f + (line * 15f), new Color32(0xFF, 0xFF, 0xFF, 0xFF), 9f + pass, face);
            }

            surface.EndDraw();
            Assert.True(surface.TryAcquire(out var frame));
            frame.Dispose();
        }

        Assert.True(lease.TextureCount > 0);
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void An_atlas_that_grows_in_the_middle_of_a_draw_still_draws_every_line(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillLease.Require(host, backend, new FontAtlasSettings { AtlasSize = 64 });
        const int Lines = 10;
        const int Pitch = 22;

        var surface = (IQuillUISurface)lease.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 420,
            Height = (Lines * Pitch) + 8,
            Scale = 1.0,
        })!;

        var face = Basin.Frames.Quill.QuillFrameFonts.Bundled();
        var canvas = surface.BeginDraw();
        var before = lease.TextureCount;
        for (var line = 0; line < Lines; line++)
        {
            var first = (char)('A' + (line * 5 % 26));
            canvas.DrawText(
                $"{first}{(char)(first + 1)}{(char)('a' + line)}{(char)('k' + line)} {line}{line * 7} glyphs {(char)('0' + line)}xyzQW",
                4f, 4f + (line * Pitch), new Color32(0xFF, 0xFF, 0xFF, 0xFF), 16f, face);
        }

        var during = lease.TextureCount;
        surface.EndDraw();
        Assert.True(during > before, $"the atlas never grew inside the draw ({before} textures before, {during} during)");

        Assert.True(surface.TryAcquire(out var frame));
        var buffer = frame.Buffer!;
        Assert.True(BufferCapture.TryReadRgba(buffer, host.Renderer, out var rgba), $"{row} did not import the chrome buffer");
        for (var line = 0; line < Lines; line++)
        {
            var lit = 0;
            for (var y = 4 + (line * Pitch); y < 4 + ((line + 1) * Pitch) && y < buffer.Height; y++)
            {
                for (var x = 0; x < buffer.Width; x++)
                {
                    if (rgba![(((y * buffer.Width) + x) * 4) + 3] > 0x80)
                    {
                        lit++;
                    }
                }
            }

            Assert.True(lit > 60, $"line {line} drew {lit} lit pixels after the atlas grew around it");
        }

        frame.Dispose();
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_popup_is_a_sibling_surface_on_the_same_host(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillLease.Require(host, backend);

        var surface = (IQuillUISurface)lease.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 200,
            Height = 60,
            Scale = 1.0,
        })!;

        var popup = surface.CreatePopup(new Box(0, 0, 180, 120), UIPopupGravity.BottomRight);
        Assert.NotNull(popup);
        var quillPopup = Assert.IsAssignableFrom<IQuillUISurface>(popup);
        Assert.True(quillPopup.Configure(180, 120, 1.0));

        Paint(quillPopup, "menu");
        Assert.True(quillPopup.TryAcquire(out var frame));
        frame.Dispose();

        popup!.Dispose();
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_texture_left_alive_is_destroyed_with_the_host(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillLease.Require(host, backend);

        var pixels = new byte[32 * 16 * 4];
        Array.Fill(pixels, (byte)0x80);
        if (lease.Gl is { } gl)
        {
            var textures = gl.Textures;
            var kept = textures.Create(32, 16);
            var dropped = textures.Create(8, 8);
            textures.Update(kept, new Box(0, 0, 32, 16), pixels);
            textures.Destroy(dropped);
        }
        else
        {
            var textures = lease.Vulkan!.Textures;
            var kept = textures.Create(32, 16);
            var dropped = textures.Create(8, 8);
            textures.Update(kept, new Box(0, 0, 32, 16), pixels);
            textures.Destroy(dropped);
        }

        Assert.Equal(1, lease.TextureCount);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_host_disposed_with_a_live_surface_takes_the_surface_with_it(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillLease.Require(host, backend);

        var surface = (IQuillUISurface)lease.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 120,
            Height = 48,
            Scale = 1.0,
        })!;

        Paint(surface, "held");
        Assert.True(surface.TryAcquire(out var frame));
        frame.Dispose();

        lease.Dispose();
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_chrome_buffer_is_composited_by_the_renderer_beside_it(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillLease.Require(host, backend);
        Assert.Equal(QuillLease.SharesDevice(host.Renderer, backend), !lease.OwnsDevice);

        var surface = (IQuillUISurface)lease.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 64,
            Height = 32,
            Scale = 1.0,
        })!;

        for (var pass = 0; pass < 3; pass++)
        {
            var canvas = surface.BeginDraw();
            canvas.SetFillColor(new Color32(0x30, (byte)(0x40 + (pass * 0x20)), 0x50, 0xFF));
            canvas.BeginPath();
            canvas.Rect(0, 0, 64, 32);
            canvas.Fill();
            surface.EndDraw();
            Assert.True(surface.TryAcquire(out var frame));

            Assert.True(BufferCapture.TryReadRgba(frame.Buffer!, host.Renderer, out var rgba), $"{row} did not import the chrome buffer");
            var i = ((16 * 64) + 32) * 4;
            Assert.InRange(rgba![i], 0x30 - 2, 0x30 + 2);
            Assert.InRange(rgba[i + 1], 0x40 + (pass * 0x20) - 2, 0x40 + (pass * 0x20) + 2);
            Assert.InRange(rgba[i + 2], 0x50 - 2, 0x50 + 2);
            Assert.Equal(0xFF, rgba[i + 3]);
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
        using var lease = QuillLease.Require(host, backend);
        var probe = lease.ProbeFence(out var devicePath);
        Assert.SkipWhen(probe < 0, $"{devicePath} exports no native fence, so every frame waits for the GPU");
        RenderFences.CloseFence(probe);

        var surface = (IQuillUISurface)lease.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = 120,
            Height = 40,
            Scale = 1.0,
        })!;

        for (var pass = 0; pass < 4; pass++)
        {
            Paint(surface, $"fence {pass}");
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
    [InlineData("pixman")]
    [InlineData("skia")]
    public void A_renderer_that_imports_no_dmabuf_is_declined_by_name(string row)
    {
        using var host = new CompositorTestHost(renderer: row);
        var quill = QuillUIHost.TryCreate(
            QuillLease.DeviceOf(host.Renderer), CompositorTestHost.RenderNodePath, host.Renderer.DmabufTextureFormats,
            out var declined);

        Assert.Null(quill);
        Assert.Contains("imports no", declined, StringComparison.Ordinal);
    }

    [Fact]
    public void No_shared_device_and_no_render_node_is_declined_by_name()
    {
        var quill = QuillUIHost.TryCreate(null, null, null, out var declined);

        Assert.Null(quill);
        Assert.Contains("render node", declined, StringComparison.Ordinal);
    }

    [Fact]
    public void An_import_set_with_no_modifier_the_device_renders_is_declined_before_any_window()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        var imports = new DrmFormatSet();
        imports.Add(DrmFormat.Argb8888, 0x00ff_ffff_ffff_fff0UL);
        using var host = new CompositorTestHost();

        var quill = QuillUIHost.TryCreate(null, CompositorTestHost.RenderNodePath, imports, out var declined);

        Assert.Null(quill);
        Assert.Contains("modifier", declined, StringComparison.Ordinal);
    }

    private static bool Covers(PixmanRegion32 region, int x, int y)
    {
        foreach (var rect in RegionRects.Of(region))
        {
            if (x >= rect.X1 && x < rect.X2 && y >= rect.Y1 && y < rect.Y2)
            {
                return true;
            }
        }

        return false;
    }

    private static void Draw(IFrameRenderer renderer, in FrameState state)
    {
        var insets = renderer.Measure(state, 1.0);
        try
        {
            renderer.Draw(
                new LayoutOnlySurface(240 + insets.Left + insets.Right, 100 + insets.Top + insets.Bottom),
                new Box(insets.Left, insets.Top, 240, 100), state, default);
        }
        catch (InvalidOperationException)
        {
        }
    }

    internal static void Paint(IQuillUISurface surface, string label)
    {
        var canvas = surface.BeginDraw();
        canvas.SetFillColor(new Color32(0x30, 0x40, 0x50, 0xFF));
        canvas.BeginPath();
        canvas.RoundedRect(0, 0, surface.Size.Width, surface.Size.Height, 6);
        canvas.Fill();
        canvas.DrawText(
            label, 6f, surface.Size.Height / 2f, new Color32(0xF0, 0xF0, 0xF0, 0xFF), 12f,
            Basin.Frames.Quill.QuillFrameFonts.Bundled(), 0f, new Float2(0f, 0.5f));
        surface.EndDraw();
    }

    internal sealed class QuillLease : IDisposable
    {
        private QuillLease(QuillUIHost host)
        {
            Host = host;
            Gl = host;
        }

        private QuillLease(QuillVulkanUIHost host)
        {
            Host = host;
            Vulkan = host;
        }

        public IUIHost Host { get; }

        public QuillUIHost? Gl { get; }

        public QuillVulkanUIHost? Vulkan { get; }

        public int TextureCount => Gl?.Textures.Count ?? Vulkan!.Textures.Count;

        public bool OwnsDevice => Gl?.OwnsDevice ?? Vulkan!.OwnsDevice;

        public int FencedFrames => Gl?.FencedFrames ?? Vulkan!.FencedFrames;

        public int FinishedFrames => Gl?.FinishedFrames ?? Vulkan!.FinishedFrames;

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

        public static QuillLease? Open(CompositorTestHost host, string backend = "gl", FontAtlasSettings? atlas = null) =>
            TryCreate(host, backend, atlas, out _);

        public static QuillLease Require(CompositorTestHost host, string backend = "gl", FontAtlasSettings? atlas = null)
        {
            var lease = TryCreate(host, backend, atlas, out var declined);
            Assert.True(lease is not null, $"the {backend} quill host declined: {declined}");
            return lease!;
        }

        public static GlDevice? DeviceOf(IRenderer renderer) => renderer switch
        {
            GlRenderer gl => gl.Device,
            Basin.Render.Skia.SkiaGlRenderer skia => skia.Device,
            _ => null,
        };

        public static Basin.Render.Vulkan.VulkanDevice? VulkanDeviceOf(IRenderer renderer) =>
            renderer.Device as Basin.Render.Vulkan.VulkanDevice;

        public static bool SharesDevice(IRenderer renderer, string backend) =>
            backend == "vulkan" ? VulkanDeviceOf(renderer) is not null : DeviceOf(renderer) is not null;

        public void Dispose() => Host.Dispose();

        private static QuillLease? TryCreate(
            CompositorTestHost host, string backend, FontAtlasSettings? atlas, out string? declined)
        {
            var node = host.Renderer.Device?.DevicePath ?? CompositorTestHost.RenderNodePath;
            if (backend == "vulkan")
            {
                var vulkan = QuillVulkanUIHost.TryCreate(
                    VulkanDeviceOf(host.Renderer), node, host.Renderer.DmabufTextureFormats, out declined, atlas: atlas);
                return vulkan is null ? null : new QuillLease(vulkan);
            }

            var gl = QuillUIHost.TryCreate(
                DeviceOf(host.Renderer), node, host.Renderer.DmabufTextureFormats, out declined, atlas: atlas);
            return gl is null ? null : new QuillLease(gl);
        }
    }

    private interface FakeSkiaSurface : IUISurface;

    private sealed class ContractHost(Type contract) : IUIHost
    {
        public Type SurfaceContract => contract;

        public UITargetKind Produces => UITargetKind.Memory;

        public long? NextDueMillis => null;

        public event Action? WakeupRequested
        {
            add
            {
            }

            remove
            {
            }
        }

        public IUISurface? CreateSurface(in UISurfaceOptions options) => null;

        public void Pump()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ProbeBackdropEffect : IBackdropEffect;

    private sealed class CountingFrameRenderer : IFrameRenderer
    {
        public int Draws { get; private set; }

        public Box Backdrop { get; init; }

        public bool BackdropRegion(in FrameState state, double scale, PixmanRegion32 into)
        {
            into.Clear();
            if (Backdrop.IsEmpty)
            {
                return false;
            }

            into.UnionRect(into, Backdrop.X, Backdrop.Y, (uint)Backdrop.Width, (uint)Backdrop.Height);
            return true;
        }

        public FrameInsets Measure(in FrameState state, double scale) => new(30, 4, 4, 4);

        public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
        {
            Draws++;
            ((FalsifierUISurface)surface).BeginPixels();
            ((FalsifierUISurface)surface).EndPixels();
        }

        public FramePart PartAt(double x, double y, in FrameState state, double scale) => FramePart.None;
    }

    private sealed class ContractRenderer(Type contract) : IFrameRenderer
    {
        public Type SurfaceContract => contract;

        public FrameInsets Measure(in FrameState state, double scale) => new(20, 2, 2, 2);

        public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
        {
        }

        public FramePart PartAt(double x, double y, in FrameState state, double scale) => FramePart.None;
    }
}
