using System.Runtime.CompilerServices;
using Basin.Effects;
using Basin.Renderers;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SmolGaussianTests
{
    private const int Size = 64;

    public static TheoryData<string> BlurRows => ["gl", "vulkan", "skia-gl", "skia-vulkan", "skia-graphite"];

    public static TheoryData<string, double, bool, int> EdgeRows => new()
    {
        { "gl", 6, true, 32 }, { "gl", 30, true, 32 }, { "gl", 60, true, 32 }, { "gl", 150, true, 32 },
        { "gl", 60, false, 32 }, { "gl", 20, true, 27 }, { "gl", 45, false, 29 },
        { "vulkan", 6, true, 32 }, { "vulkan", 30, true, 32 }, { "vulkan", 60, true, 32 }, { "vulkan", 150, true, 32 },
        { "vulkan", 60, false, 32 }, { "vulkan", 20, true, 27 }, { "vulkan", 45, false, 29 },
        { "skia-gl", 30, true, 32 }, { "skia-vulkan", 30, true, 32 }, { "skia-graphite", 30, true, 32 },
    };

    private static string ShaderDirectory([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "src", "Basin.Effects", "shaders"));

    [Theory]
    [InlineData("smol_down.frag", nameof(SmolGaussianShaders.Down))]
    [InlineData("smol_down4.frag", nameof(SmolGaussianShaders.Down4))]
    [InlineData("smol_gauss.frag", nameof(SmolGaussianShaders.Gauss))]
    [InlineData("smol_reconstruct.frag", nameof(SmolGaussianShaders.Reconstruct))]
    public void The_checked_in_fragment_is_the_vulkan_header_and_the_shared_body(string file, string body)
    {
        var text = body switch
        {
            nameof(SmolGaussianShaders.Down) => SmolGaussianShaders.Down,
            nameof(SmolGaussianShaders.Down4) => SmolGaussianShaders.Down4,
            nameof(SmolGaussianShaders.Gauss) => SmolGaussianShaders.Gauss,
            _ => SmolGaussianShaders.Reconstruct,
        };
        var frag = File.ReadAllText(Path.Combine(ShaderDirectory(), file));

        Assert.Equal(SmolGaussianShaders.VulkanHeader + "\n" + text + "\n", frag);
    }

    [Theory]
    [InlineData(17.9, 0)]
    [InlineData(18, 1)]
    [InlineData(35.9, 1)]
    [InlineData(36, 2)]
    [InlineData(72, 3)]
    [InlineData(144, 4)]
    public void A_level_starts_where_the_working_sigma_reaches_six(double radius, int level)
    {
        Assert.Equal(level, SmolAxis.For(radius / 3, 1024).Level);
    }

    [Fact]
    public void A_single_texel_axis_stops_reducing_and_does_not_filter()
    {
        var axis = SmolAxis.For(500, 1);

        Assert.Equal(0, axis.Level);
        Assert.False(axis.Filters);
    }

    [Fact]
    public void The_residual_sigma_leaves_room_for_the_reduction_and_the_reconstruction()
    {
        var axis = SmolAxis.For(20, 1024);
        var scale = 1024.0 / axis.Working;
        var total = (axis.Sigma * axis.Sigma * scale * scale) + (((scale * scale) - 1) / 12) + (scale * scale / 3);

        Assert.Equal(2, axis.Level);
        Assert.Equal(400, total, 6);
    }

    [Fact]
    public void Two_exact_halves_fuse_into_one_quarter()
    {
        var x = SmolAxis.For(50, 1024);
        var y = SmolAxis.For(50, 1024);
        var steps = new SmolSteps(x, y);
        var fused = 0;
        var passes = 0;
        while (steps.Next(out var levelX, out var levelY, out var quarter))
        {
            passes++;
            fused += quarter ? 1 : 0;
            Assert.Equal(levelX, levelY);
        }

        Assert.Equal(4, x.Level);
        Assert.Equal(2, passes);
        Assert.Equal(2, fused);
    }

    [Fact]
    public void An_odd_size_reduces_by_area_before_it_fuses()
    {
        var x = SmolAxis.For(100, 999);
        var y = SmolAxis.For(100, 999);
        var steps = new SmolSteps(x, y);
        var visited = new List<(int, int, bool)>();
        while (steps.Next(out var levelX, out var levelY, out var quarter))
        {
            visited.Add((levelX, levelY, quarter));
        }

        Assert.Equal((x.Level, y.Level), (visited[^1].Item1, visited[^1].Item2));
        Assert.Equal((1, 1, false), visited[0]);
        Assert.Contains(visited, step => step.Item3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void A_strength_radius_has_the_spread_of_its_dual_kawase_passes(int strength)
    {
        var step = BlurStrength.For(strength);
        var sigma = KawaseSigma(step.Iterations, step.Offset);

        Assert.Equal(3 * sigma, step.Radius, 3 * sigma * 0.01);
    }

    [Theory]
    [MemberData(nameof(EdgeRows))]
    public void An_edge_blurs_like_a_gaussian_of_the_radius(string row, double radius, bool vertical, int extent)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var rgba = BackdropBlurTests.RenderEdge(
            row, vertical, blur => blur.Options = blur.Options with
            {
                Kernel = BlurKernel.SmolGaussian,
                Radius = radius,
                NoiseStrength = 0,
            },
            veilWidth: vertical ? extent : 32,
            veilHeight: vertical ? 32 : extent);
        var expected = GaussianEdge(radius / 3, extent, linear: row == "vulkan");
        var worst = 0.0;
        for (var i = 16; i < 16 + extent; i++)
        {
            var x = vertical ? i : 32;
            var y = vertical ? 32 : i;
            worst = Math.Max(worst, Math.Abs(rgba[(y * Size + x) * 4] - expected[i]));
        }

        Assert.True(worst <= 2, $"max delta {worst:F1} on {row} at radius {radius}");
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_surface_radius_overrides_the_shared_one(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var key = new object();
        var keyed = BackdropBlurTests.RenderEdge(row, vertical: true, blur =>
        {
            blur.Options = blur.Options with { Kernel = BlurKernel.SmolGaussian, Radius = 6 };
            blur.SetSurface(key, new BlurSurfaceOptions { Radius = 40 });
        }, key);
        var global = BackdropBlurTests.RenderEdge(row, vertical: true, blur => blur.Options = blur.Options with { Kernel = BlurKernel.SmolGaussian, Radius = 40 });
        var narrow = BackdropBlurTests.RenderEdge(row, vertical: true, blur => blur.Options = blur.Options with { Kernel = BlurKernel.SmolGaussian, Radius = 6 });

        AssertSame(global, keyed);
        Assert.NotEqual(narrow, keyed);
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_strength_without_a_radius_blurs_at_the_strength_radius(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var strength = BackdropBlurTests.RenderEdge(row, vertical: true, blur => blur.Options = blur.Options with { Kernel = BlurKernel.SmolGaussian, Strength = 4 });
        var radius = BackdropBlurTests.RenderEdge(row, vertical: true, blur => blur.Options = blur.Options with
        {
            Kernel = BlurKernel.SmolGaussian,
            Strength = 4,
            Radius = BlurStrength.For(4).Radius,
        });

        AssertSame(radius, strength);
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void Golden_smol_frosted_node(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipUnless(CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this device");
        var rgba = BackdropBlurTests.RenderFrosted(row, blur => blur.Options = blur.Options with { Kernel = BlurKernel.SmolGaussian, Radius = 24 }, key: null);
        Golden.AssertMatches(rgba, Size, Size, $"smol-frosted-node-{row}", tolerance: 2);
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_steady_smol_backdrop_allocates_nothing(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var floor = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        var target = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        var blur = BackdropBlurs.For(stack.Renderer);
        try
        {
            Assert.NotNull(blur);
            blur.Options = blur.Options with { Kernel = BlurKernel.SmolGaussian, Radius = 40 };
            Fill(floor, 0xFF808080);
            Fill(veil, 0x40102030);
            var under = new SceneBuffer(scene.Root);
            under.SetBuffer(floor);
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
            under.Destroy();
        }
        finally
        {
            blur?.Dispose();
            target.Destroy();
            veil.Destroy();
            floor.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(BlurRows))]
    public void A_target_resized_many_times_keeps_only_live_smol_levels(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var floor = new MemoryBuffer(Size, Size, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(32, 32, DrmFormat.Argb8888);
        using var region = new Pixman.PixmanRegion32(0, 0, 32, 32);
        var blur = BackdropBlurs.For(stack.Renderer);
        try
        {
            Assert.NotNull(blur);
            blur.Options = blur.Options with { Kernel = BlurKernel.SmolGaussian, Radius = 60 };
            Fill(floor, 0xFF808080);
            Fill(veil, 0x40102030);
            var under = new SceneBuffer(scene.Root);
            under.SetBuffer(floor);
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
            under.Destroy();
        }
        finally
        {
            blur?.Dispose();
            veil.Destroy();
            floor.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static double[] GaussianEdge(double sigma, int extent, bool linear)
    {
        double Source(int x) => Math.Clamp(x, 16, 15 + extent) < Size / 2 ? 0 : 1;
        var reach = (int)Math.Ceiling(6 * sigma) + 1;
        var result = new double[Size];
        for (var x = 16; x < 16 + extent; x++)
        {
            var sum = 0.0;
            var total = 0.0;
            for (var k = -reach; k <= reach; k++)
            {
                var weight = Math.Exp(-0.5 * k * k / (sigma * sigma));
                sum += weight * Source(x - k);
                total += weight;
            }

            var value = sum / total;
            result[x] = 255 * (linear ? Encode(value) : value);
        }

        return result;
    }

    private static double Encode(double value)
    {
        return value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
    }

    private static double KawaseSigma(int iterations, double offset)
    {
        const int length = 1024;
        var h = 0.5 * offset;
        var variance = 0.0;
        var phases = 0;
        for (var phase = 0; phase < 1 << iterations; phase += Math.Max(1, (1 << iterations) / 8))
        {
            var levels = new List<double[]> { new double[length] };
            levels[0][(length / 2) + phase] = 1;
            for (var i = 0; i < iterations; i++)
            {
                var source = levels[^1];
                var down = new double[source.Length / 2];
                for (var x = 0; x < down.Length; x++)
                {
                    var p = (2 * x) + 1;
                    down[x] = (0.5 * Sample(source, p)) + (0.25 * Sample(source, p - h)) + (0.25 * Sample(source, p + h));
                }

                levels.Add(down);
            }

            var current = levels[^1];
            for (var i = iterations - 1; i >= 0; i--)
            {
                var up = new double[levels[i].Length];
                for (var x = 0; x < up.Length; x++)
                {
                    var p = (x + 0.5) * 0.5;
                    up[x] = ((Sample(current, p - (2 * h)) + Sample(current, p + (2 * h))) / 12)
                        + (4.0 / 12 * (Sample(current, p - h) + Sample(current, p + h)))
                        + (2.0 / 12 * Sample(current, p));
                }

                current = up;
            }

            var mass = 0.0;
            var mean = 0.0;
            for (var x = 0; x < length; x++)
            {
                mass += current[x];
                mean += current[x] * x;
            }

            mean /= mass;
            var spread = 0.0;
            for (var x = 0; x < length; x++)
            {
                spread += current[x] * (x - mean) * (x - mean);
            }

            variance += spread / mass;
            phases++;
        }

        return Math.Sqrt(variance / phases);
    }

    private static double Sample(double[] source, double position)
    {
        var i = (int)Math.Floor(position - 0.5);
        var f = position - 0.5 - i;
        var a = source[Math.Clamp(i, 0, source.Length - 1)];
        var b = source[Math.Clamp(i + 1, 0, source.Length - 1)];
        return (a * (1 - f)) + (b * f);
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

    private static unsafe void Fill(MemoryBuffer buffer, uint pixel)
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
