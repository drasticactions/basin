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

    public static TheoryData<string> NoBlurRows => ["pixman", "skia", "impeller"];

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
