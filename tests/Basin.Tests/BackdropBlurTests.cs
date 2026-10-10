using Basin.Diagnostics;
using Basin.Effects;
using Basin.Renderers;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class BackdropBlurTests
{
    private const int Size = 64;

    public static TheoryData<string> BlurRows => ["gl", "vulkan", "skia-gl", "skia-vulkan", "skia-graphite"];

    public static TheoryData<string, string, int> SkiaRows => new() { { "skia-gl", "gl", 3 }, { "skia-vulkan", "gl", 12 }, { "skia-graphite", "gl", 12 } };

    public static TheoryData<string> NoBlurRows => ["pixman", "skia"];

    [Theory]
    [MemberData(nameof(NoBlurRows))]
    public void A_row_without_backdrops_gets_no_blur(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        try
        {
            Assert.Null(BackdropBlurs.For(stack.Renderer));
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(NoBlurRows))]
    public void A_row_without_backdrops_draws_a_frosted_node_unblurred(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var effect = new ForeignEffect();
        var frosted = RenderFrosted(row, configure: null, key: null, foreign: effect);
        var plain = RenderFrosted(row, configure: null, key: null, foreign: null);

        Assert.Equal(plain, frosted);
    }

    private sealed class ForeignEffect : IBackdropEffect
    {
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_row_with_backdrops_gets_the_blur_for_its_device(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        try
        {
            using var blur = BackdropBlurs.For(stack.Renderer);
            Assert.NotNull(blur);
            Assert.Equal(row is "gl" or "skia-gl", blur is GlBackdropBlur);
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_key_shallower_than_the_chain_blurs_as_its_own_strength(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var key = new object();
        var keyed = RenderFrosted(row, blur =>
        {
            blur.Options = blur.Options with { Strength = BlurStrength.Steps };
            blur.SetSurface(key, new BlurSurfaceOptions { Strength = 2 });
        }, key);
        var plain = RenderFrosted(row, blur => blur.Options = blur.Options with { Strength = 2 }, key: null);

        AssertSame(plain, keyed);
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_key_deeper_than_the_options_grows_the_chain(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var key = new object();
        var keyed = RenderFrosted(row, blur =>
        {
            blur.Options = blur.Options with { Strength = 2 };
            blur.SetSurface(key, new BlurSurfaceOptions { Strength = BlurStrength.Steps });
        }, key);
        var plain = RenderFrosted(row, blur => blur.Options = blur.Options with { Strength = BlurStrength.Steps }, key: null);
        var shallow = RenderFrosted(row, blur => blur.Options = blur.Options with { Strength = 2 }, key: null);

        AssertSame(plain, keyed);
        Assert.NotEqual(shallow, keyed);
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void The_expand_size_is_the_widest_live_strength(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        try
        {
            using var blur = BackdropBlurs.For(stack.Renderer)!;
            blur.Options = blur.Options with { Strength = 1 };
            Assert.Equal(BlurStrength.For(1).ExpandSize, blur.ExpandSize);

            var key = new object();
            blur.SetSurface(key, new BlurSurfaceOptions { Strength = BlurStrength.Steps });
            Assert.Equal(BlurStrength.For(BlurStrength.Steps).ExpandSize, blur.ExpandSize);
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(SkiaRows))]
    public void A_skia_row_blurs_like_the_gamma_space_reference(string row, string reference, int tolerance)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(reference);
        var skia = RenderFrosted(row, blur => blur.Options = blur.Options with { Strength = 6 }, key: null);
        var native = RenderFrosted(reference, blur => blur.Options = blur.Options with { Strength = 6 }, key: null);

        var worst = 0;
        for (var i = 0; i < skia.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(skia[i] - native[i]));
        }

        Assert.True(worst <= tolerance, $"max channel delta {worst} against {reference}");
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void Golden_frosted_node(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipUnless(CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this device");
        var rgba = RenderFrosted(row, blur => blur.Options = blur.Options with { Strength = 6 }, key: null);
        Golden.AssertMatches(rgba, Size, Size, $"frosted-node-{row}", tolerance: 2);
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_target_resized_many_times_keeps_only_live_pyramids(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var checker = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        var blur = BackdropBlurs.For(stack.Renderer);
        try
        {
            Assert.NotNull(blur);
            Checker(checker);
            Solid(veil, 0x40102030);
            var floor = new SceneBuffer(scene.Root);
            floor.SetBuffer(checker);
            var node = new SceneBuffer(scene.Root);
            node.SetPosition(16, 16);
            node.SetBuffer(veil);
            node.SetBackdropEffect(blur, region, key: null);

            for (var i = 0; i < 100; i++)
            {
                var target = new MemoryBuffer(Size + i, Size + (i % 7), DrmFormat.Xrgb8888);
                try
                {
                    Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
                }
                finally
                {
                    target.Destroy();
                }
            }

            var pyramids = blur switch
            {
                GlBackdropBlur gl => gl.PyramidCount,
                VulkanBackdropBlur vulkan => vulkan.PyramidCount,
                _ => throw new InvalidOperationException(blur.GetType().Name),
            };
            Assert.True(pyramids <= (blur is GlBackdropBlur ? 0 : 2), $"{pyramids} pyramids outlived their targets");

            node.Destroy();
            floor.Destroy();
        }
        finally
        {
            blur?.Dispose();
            veil.Destroy();
            checker.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    public static TheoryData<string, bool> DwmRows => new()
    {
        { "gl", true }, { "gl", false }, { "vulkan", true }, { "vulkan", false },
        { "skia-gl", true }, { "skia-vulkan", true },
    };

    [Theory]
    [MemberData(nameof(DwmRows))]
    public void The_vista_dwm_kernel_matches_the_traced_weights(string row, bool vertical)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var rgba = RenderEdge(row, vertical, blur => blur.Options = blur.Options with { Kernel = BlurKernel.VistaDwm });
        var expected = SimulateDwm(vertical);
        var worst = 0;
        for (var i = 16; i < 48; i++)
        {
            var x = vertical ? i : 32;
            var y = vertical ? 32 : i;
            var actual = rgba[(y * Size + x) * 4];
            worst = Math.Max(worst, Math.Abs(actual - expected[i]));
        }

        Assert.True(worst <= 1, $"max delta {worst} on {row}, {(vertical ? "vertical" : "horizontal")} edge");
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    [InlineData("skia-vulkan")]
    public void The_vista_dwm_kernel_blurs_encoded_values(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var rgba = RenderEdge(row, vertical: true, blur => blur.Options = blur.Options with { Kernel = BlurKernel.VistaDwm }, low: 0x40, high: 0xC0);
        var expected = SimulateDwm(vertical: true, low: 0x40, high: 0xC0);
        var worst = 0;
        for (var x = 16; x < 48; x++)
        {
            worst = Math.Max(worst, Math.Abs(rgba[(32 * Size + x) * 4] - expected[x]));
        }

        Assert.True(worst <= 1, $"max delta {worst} on {row}");
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_surface_can_choose_the_vista_dwm_kernel(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var key = new object();
        var keyed = RenderEdge(row, vertical: true, blur => blur.SetSurface(key, new BlurSurfaceOptions { Kernel = BlurKernel.VistaDwm }), key);
        var global = RenderEdge(row, vertical: true, blur => blur.Options = blur.Options with { Kernel = BlurKernel.VistaDwm });
        var kawase = RenderEdge(row, vertical: true, configure: null);

        AssertSame(global, keyed);
        Assert.NotEqual(kawase, keyed);
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void The_vista_dwm_kernel_widens_the_expand_size(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        try
        {
            using var blur = BackdropBlurs.For(stack.Renderer)!;
            blur.Options = blur.Options with { Strength = 1 };
            var kawase = blur.ExpandSize;
            blur.SetSurface(new object(), new BlurSurfaceOptions { Kernel = BlurKernel.VistaDwm });
            Assert.True(blur.ExpandSize >= VistaDwmKernel.ExpandSize);
            Assert.True(blur.ExpandSize >= kawase);
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_steady_vista_dwm_backdrop_allocates_nothing(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var checker = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        var target = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        var blur = BackdropBlurs.For(stack.Renderer);
        try
        {
            Assert.NotNull(blur);
            blur.Options = blur.Options with { Kernel = BlurKernel.VistaDwm };
            Checker(checker);
            Solid(veil, 0x45409efe);
            var floor = new SceneBuffer(scene.Root);
            floor.SetBuffer(checker);
            var node = new SceneBuffer(scene.Root);
            node.SetPosition(16, 16);
            node.SetBuffer(veil);
            node.SetBackdropEffect(blur, region, key: null);
            for (var i = 0; i < 20; i++)
            {
                Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 200; i++)
            {
                scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1));
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            node.Destroy();
            floor.Destroy();
        }
        finally
        {
            blur?.Dispose();
            target.Destroy();
            veil.Destroy();
            checker.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    internal static int[] SimulateDwm(bool vertical, int low = 0, int high = 255)
    {
        var full = new double[Size];
        for (var i = 0; i < Size; i++)
        {
            full[i] = i < Size / 2 ? low : high;
        }

        var half = new double[Size / 2];
        for (var j = 0; j < half.Length; j++)
        {
            half[j] = (full[2 * j] + full[2 * j + 1]) / 2;
        }

        var weights = new double[4];
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                weights[vertical ? c : r] += VistaDwmKernel.Weights[r * 4 + c];
            }
        }

        double Sample(double t)
        {
            t = Math.Clamp(t, 8.5, 23.5);
            var i = (int)Math.Floor(t - 0.5);
            var f = t - 0.5 - i;
            return half[i] * (1 - f) + half[Math.Min(i + 1, half.Length - 1)] * f;
        }

        var result = new int[Size];
        for (var x = 0; x < Size; x++)
        {
            var u = (x + 0.5) / 2;
            var sum = 0.0;
            for (var k = 0; k < 4; k++)
            {
                sum += weights[k] * Sample(u + VistaDwmKernel.Offsets[k]);
            }

            result[x] = (int)Math.Round(Math.Clamp(sum, 0, 255));
        }

        return result;
    }

    internal static byte[] RenderEdge(string row, bool vertical, Action<IBackdropBlur>? configure, object? key = null, int low = 0, int high = 255)
    {
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var edge = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        var target = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        var blur = BackdropBlurs.For(stack.Renderer);
        try
        {
            Edge(edge, vertical, low, high);
            Solid(veil, 0x00000000);
            var floor = new SceneBuffer(scene.Root);
            floor.SetBuffer(edge);
            var node = new SceneBuffer(scene.Root);
            node.SetPosition(16, 16);
            node.SetBuffer(veil);
            Assert.NotNull(blur);
            configure?.Invoke(blur);
            node.SetBackdropEffect(blur, region, key);
            Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
            var rgba = BufferCapture.ReadRgba(target);
            node.Destroy();
            floor.Destroy();
            return rgba;
        }
        finally
        {
            blur?.Dispose();
            target.Destroy();
            veil.Destroy();
            edge.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static unsafe void Edge(MemoryBuffer buffer, bool vertical, int low, int high)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = (uint*)(view.Data + y * view.Stride);
            for (var x = 0; x < buffer.Width; x++)
            {
                var level = (uint)((vertical ? x : y) < buffer.Width / 2 ? low : high);
                row[x] = 0xFF000000u | (level << 16) | (level << 8) | level;
            }
        }

        buffer.EndDataAccess();
    }

    internal static byte[] RenderFrosted(string row, Action<IBackdropBlur>? configure, object? key, IBackdropEffect? foreign = null)
    {
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var checker = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        var target = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        var blur = BackdropBlurs.For(stack.Renderer);
        try
        {
            Checker(checker);
            Solid(veil, 0x40102030);
            var floor = new SceneBuffer(scene.Root);
            floor.SetBuffer(checker);
            var node = new SceneBuffer(scene.Root);
            node.SetPosition(16, 16);
            node.SetBuffer(veil);
            if (foreign is not null)
            {
                node.SetBackdropEffect(foreign, region, key);
            }
            else if (blur is not null)
            {
                configure?.Invoke(blur);
                node.SetBackdropEffect(blur, region, key);
            }

            Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
            var rgba = BufferCapture.ReadRgba(target);
            node.Destroy();
            floor.Destroy();
            return rgba;
        }
        finally
        {
            blur?.Dispose();
            target.Destroy();
            veil.Destroy();
            checker.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static void AssertSame(byte[] expected, byte[] actual)
    {
        var worst = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(expected[i] - actual[i]));
        }

        Assert.True(worst <= 1, $"max channel delta {worst}");
    }

    private static unsafe void Checker(MemoryBuffer buffer)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = (uint*)(view.Data + y * view.Stride);
            for (var x = 0; x < buffer.Width; x++)
            {
                row[x] = ((x / 4) + (y / 4)) % 2 == 0 ? 0xFFF0F0F0u : 0xFF202020u;
            }
        }

        buffer.EndDataAccess();
    }

    private static unsafe void Solid(MemoryBuffer buffer, uint pixel)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = (uint*)(view.Data + y * view.Stride);
            for (var x = 0; x < buffer.Width; x++)
            {
                row[x] = pixel;
            }
        }

        buffer.EndDataAccess();
    }
}
