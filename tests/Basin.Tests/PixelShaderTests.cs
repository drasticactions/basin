using System.Runtime.CompilerServices;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class PixelShaderTests
{
    internal static byte[] SpirV(string name, [CallerFilePath] string sourcePath = "") =>
        File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(sourcePath)!, "Shaders", $"{name}.frag.spv"));

    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller" };

    private static string GoldenName(string name, string renderer) =>
        renderer == "pixman" ? name : $"{name}-{renderer}";

    internal static readonly PixelShaderUniform[] RingUniforms =
    [
        new("center", PixelShaderUniformType.Float2),
        new("radius", PixelShaderUniformType.Float),
    ];

    internal static PixelShaderSource RingSource => new()
    {
        Glsl = """
            vec4 basin_pixel(vec2 coord) {
                float d = distance(coord, center);
                float disc = 1.0 - smoothstep(radius - 12.0, radius, d);
                vec3 rgb = mix(vec3(0.1, 0.2, 0.8), vec3(0.9, 0.6, 0.1), coord.x / u_size.x);
                return vec4(rgb * disc, disc);
            }
            """,
        Sksl = """
            half4 basin_pixel(float2 coord) {
                float d = distance(coord, center);
                float disc = 1.0 - smoothstep(radius - 12.0, radius, d);
                half3 rgb = half3(mix(float3(0.1, 0.2, 0.8), float3(0.9, 0.6, 0.1), coord.x / u_size.x));
                return half4(rgb * half(disc), half(disc));
            }
            """,
        SpirV = SpirV("ring"),
    };

    internal static readonly PixelShaderUniform[] SplitUniforms =
    [
        new("split", PixelShaderUniformType.Float),
    ];

    internal static PixelShaderSource SplitSource => new()
    {
        SamplesTexture = true,
        Glsl = """
            vec4 basin_pixel(vec2 coord) {
                vec4 c = basin_texture(coord);
                float g = dot(c.rgb, vec3(0.299, 0.587, 0.114));
                float mask = step(split, coord.x / u_size.x);
                return mix(c, vec4(vec3(g), c.a), mask);
            }
            """,
        Sksl = """
            half4 basin_pixel(float2 coord) {
                half4 c = basin_texture(coord);
                half g = dot(c.rgb, half3(0.299, 0.587, 0.114));
                half mask = half(step(split, coord.x / u_size.x));
                return mix(c, half4(half3(g), c.a), mask);
            }
            """,
        SpirV = SpirV("split"),
    };

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_pixel_shader(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(RingSource, RingUniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the test shader");

        shader.SetUniforms([(54f, 40f), 30f]);
        var target = new MemoryBuffer(128, 96, DrmFormat.Xrgb8888);
        using var guard = new DeferDestroy(target);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.12f, 0.12f, 0.14f, 1f), new Box(0, 0, 128, 96));
        pass.AddShader(shader, new ShaderRenderOptions { DstBox = new Box(10, 8, 108, 80) });
        Assert.True(pass.Submit());
        Golden.AssertMatches(target, GoldenName("pixel-shader", renderer), renderer == "pixman" ? 0 : 1);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_pixel_shader_alpha_and_clip(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(RingSource, RingUniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the test shader");

        shader.SetUniforms([(64f, 48f), 44f]);
        var target = new MemoryBuffer(128, 96, DrmFormat.Xrgb8888);
        using var guard = new DeferDestroy(target);
        using var clip = new Pixman.PixmanRegion32(0, 0, 96, 96);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.3f, 0.3f, 0.3f, 1f), new Box(0, 0, 128, 96));
        pass.AddShader(shader, new ShaderRenderOptions { DstBox = new Box(0, 0, 128, 96), Alpha = 0.5f, Clip = clip });
        Assert.True(pass.Submit());
        Golden.AssertMatches(target, GoldenName("pixel-shader-alpha", renderer), renderer == "pixman" ? 0 : 1);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_texture_shader(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(SplitSource, SplitUniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the test shader");

        shader.SetUniforms([0.5f]);
        var source = new MemoryBuffer(64, 48, DrmFormat.Argb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
        Fill.Gradient(64, 48)(view.Data, view.Stride);
        source.EndDataAccess();
        using var sourceGuard = new DeferDestroy(source);
        var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);

        var target = new MemoryBuffer(128, 96, DrmFormat.Xrgb8888);
        using var guard = new DeferDestroy(target);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.12f, 0.12f, 0.14f, 1f), new Box(0, 0, 128, 96));
        pass.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(16, 12, 96, 72), Shader = shader });
        Assert.True(pass.Submit());
        texture!.Dispose();
        Golden.AssertMatches(target, GoldenName("texture-shader", renderer), renderer == "pixman" ? 0 : 1);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Uniform_count_mismatch_throws(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(RingSource, RingUniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the test shader");

        Assert.Throws<ArgumentException>(() => shader.SetUniforms([1f]));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Bad_source_throws_with_the_compiler_log(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var probe = host.Renderer.CompilePixelShader(RingSource, RingUniforms);
        Assert.SkipWhen(probe is null, $"{renderer} compiles no dialect of the test shader");
        Assert.SkipWhen(renderer == "vulkan", "SPIR-V is precompiled and carries no compiler log");

        var broken = new PixelShaderSource
        {
            Glsl = "vec4 basin_pixel(vec2 coord) { return not_a_symbol; }",
            Sksl = "half4 basin_pixel(float2 coord) { return not_a_symbol; }",
        };
        Assert.Throws<InvalidOperationException>(() => host.Renderer.CompilePixelShader(broken, []));
    }

    [Fact]
    public void Pixman_compiles_nothing()
    {
        using var host = new CompositorTestHost();
        Assert.Null(host.Renderer.CompilePixelShader(RingSource, RingUniforms));
    }

    [Fact]
    public void A_shader_that_samples_must_not_fill()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var host = new CompositorTestHost(renderer: "gl");
        using var shader = host.Renderer.CompilePixelShader(SplitSource, SplitUniforms);
        Assert.NotNull(shader);

        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        using var guard = new DeferDestroy(target);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        Assert.Throws<ArgumentException>(() => pass.AddShader(shader!, new ShaderRenderOptions { DstBox = new Box(0, 0, 64, 64) }));
        Assert.True(pass.Submit());
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Scene_shader_matches_between_oracle_and_scene_output(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(RingSource, RingUniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the test shader");

        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        shader!.SetUniforms([(40f, 30f), 25f]);
        var node = new SceneShader(host.Scene.Root) { Shader = shader, Bounds = new Box(10, 10, 80, 60) };

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!, "shader fill");

        shader.SetUniforms([(60f, 30f), 20f]);
        node.NotifyShaderChanged();
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!, "uniform change");

        node.Destroy();
        oracle.Destroy();
    }

    [Fact]
    public void A_scene_shader_with_no_handle_draws_nothing()
    {
        using var host = new CompositorTestHost();
        var node = new SceneShader(host.Scene.Root) { Bounds = new Box(10, 10, 80, 60) };
        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 0 || rgba[i + 1] != 0 || rgba[i + 2] != 0)
            {
                Assert.Fail("a shader node without a compiled handle must stay invisible");
            }
        }

        node.Destroy();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_transformed_group_keeps_the_texture_shader(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var scaleState = new OutputState();
        Assert.True(host.Output.Commit(scaleState.SetScale(2.0)));

        var absoluteSplit = new PixelShaderSource
        {
            SamplesTexture = true,
            Glsl = """
                vec4 basin_pixel(vec2 coord) {
                    vec4 c = basin_texture(coord);
                    float g = dot(c.rgb, vec3(0.299, 0.587, 0.114));
                    return mix(c, vec4(vec3(g), c.a), step(split, coord.x));
                }
                """,
            Sksl = """
                half4 basin_pixel(float2 coord) {
                    half4 c = basin_texture(coord);
                    half g = dot(c.rgb, half3(0.299, 0.587, 0.114));
                    return mix(c, half4(half3(g), c.a), half(step(split, coord.x)));
                }
                """,
            SpirV = SpirV("abs_split"),
        };
        using var shader = host.Renderer.CompilePixelShader(absoluteSplit, SplitUniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the test shader");
        shader!.SetUniforms([64f]);

        var source = new MemoryBuffer(64, 48, DrmFormat.Argb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
        Fill.Gradient(64, 48)(view.Data, view.Stride);
        source.EndDataAccess();
        using var sourceGuard = new DeferDestroy(source);

        var tree = new SceneTree(host.Scene.Root);
        var transform = new SceneTransform(tree);
        transform.Alpha = 0.9f;
        transform.Matrix = RenderTransform.Multiply(
            RenderTransform.Translation(8, 6),
            RenderTransform.Scale(0.8, 0.8));
        var node = new SceneBuffer(transform)
        {
            DestinationWidth = 64,
            DestinationHeight = 48,
            TextureShader = shader,
        };
        node.SetBuffer(source);

        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int Chroma(int x, int y)
        {
            var i = ((y * host.Target.Width) + x) * 4;
            return Math.Max(Math.Abs(rgba[i] - rgba[i + 1]), Math.Abs(rgba[i + 1] - rgba[i + 2]));
        }

        Assert.True(Chroma(40, 40) > 6, "left of the split keeps the gradient colors");
        Assert.True(Chroma(100, 40) <= 3, "right of the split is grayscale, so the split sits at 64 physical pixels");
        tree.Destroy();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_texture_shader_composes_with_a_lut(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(SplitSource, SplitUniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the test shader");
        shader!.SetUniforms([0.5f]);

        var lutData = new float[2 * 2 * 2 * 3];
        var index = 0;
        for (var b = 0; b < 2; b++)
        {
            for (var g = 0; g < 2; g++)
            {
                for (var r = 0; r < 2; r++)
                {
                    lutData[index++] = 1f - r;
                    lutData[index++] = 1f - g;
                    lutData[index++] = 1f - b;
                }
            }
        }

        var lut = host.Renderer.ImportLut(new ColorLut3D(2, lutData));
        Assert.NotNull(lut);

        var source = new MemoryBuffer(64, 48, DrmFormat.Argb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
        Fill.Gradient(64, 48)(view.Data, view.Stride);
        source.EndDataAccess();
        using var sourceGuard = new DeferDestroy(source);
        var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);

        byte[] Draw(Basin.IColorLut? withLut)
        {
            var target = new MemoryBuffer(64, 48, DrmFormat.Xrgb8888);
            var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddTexture(texture!, new TextureRenderOptions
            {
                DstBox = new Box(0, 0, 64, 48),
                Shader = shader,
                Lut = withLut,
            });
            Assert.True(pass.Submit());
            var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(target);
            target.Destroy();
            return rgba;
        }

        var plain = Draw(null);
        var managed = Draw(lut);
        var i2 = ((40 * 64) + 20) * 4;
        for (var c = 0; c < 3; c++)
        {
            Assert.True(
                Math.Abs(255 - plain[i2 + c] - managed[i2 + c]) <= 6,
                $"channel {c}: plain {plain[i2 + c]} managed {managed[i2 + c]} is not the inversion the LUT demands");
        }

        if (renderer != "vulkan")
        {
            var i3 = ((40 * 64) + 50) * 4;
            Assert.True(
                Math.Abs(managed[i3] - managed[i3 + 1]) <= 3 && Math.Abs(managed[i3 + 1] - managed[i3 + 2]) <= 3,
                "right of the split stays grayscale, so the shader still ran over the color-managed texels");
        }

        texture!.Dispose();
        lut!.Dispose();
    }

    [Fact]
    public void A_snapshot_keeps_the_texture_shader_of_its_source()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        using var host = new CompositorTestHost(renderer: "gl");
        using var shader = host.Renderer.CompilePixelShader(SplitSource, SplitUniforms);
        Assert.NotNull(shader);
        shader!.SetUniforms([0f]);

        var source = new MemoryBuffer(64, 48, DrmFormat.Argb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
        Fill.Gradient(64, 48)(view.Data, view.Stride);
        source.EndDataAccess();
        using var sourceGuard = new DeferDestroy(source);

        var tree = new SceneTree(host.Scene.Root);
        var node = new SceneBuffer(tree)
        {
            DestinationWidth = 64,
            DestinationHeight = 48,
            TextureShader = shader,
        };
        node.SetBuffer(source);

        var snapshot = SceneSnapshot.Capture(tree, host.Scene.Root);
        tree.Destroy();

        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        var i = ((20 * host.Target.Width) + 40) * 4;
        Assert.True(
            Math.Abs(rgba[i] - rgba[i + 1]) <= 2 && Math.Abs(rgba[i + 1] - rgba[i + 2]) <= 2,
            "the snapshot draws through the copied shader, so the pixel is grayscale");

        snapshot.Destroy();
    }

    private static void AssertSamePixels(IBuffer expected, IBuffer actual, string what)
    {
        Assert.True(expected.BeginDataAccess(BufferDataAccess.Read, out var e), what);
        Assert.True(actual.BeginDataAccess(BufferDataAccess.Read, out var a), what);
        try
        {
            unsafe
            {
                for (var y = 0; y < expected.Height; y++)
                {
                    var expectedRow = new ReadOnlySpan<byte>((void*)(e.Data + (y * e.Stride)), expected.Width * 4);
                    var actualRow = new ReadOnlySpan<byte>((void*)(a.Data + (y * a.Stride)), expected.Width * 4);
                    if (!expectedRow.SequenceEqual(actualRow))
                    {
                        Assert.Fail($"{what}: row {y} differs");
                    }
                }
            }
        }
        finally
        {
            actual.EndDataAccess();
            expected.EndDataAccess();
        }
    }

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }
}
