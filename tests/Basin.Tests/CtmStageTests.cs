using Basin.Effects;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class CtmStageTests
{
    private sealed class BufferGuard(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }

    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller" };

    private static (byte R, byte G, byte B) Through(CompositorTestHost host, CtmStage stage, RenderColor input)
    {
        var source = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        using var sourceGuard = new BufferGuard(source);
        var fill = host.Renderer.BeginBufferPass(source, new RenderPassOptions());
        fill.AddRect(input, new Box(0, 0, 32, 32));
        Assert.True(fill.Submit());

        using var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);
        var target = new MemoryBuffer(32, 32, DrmFormat.Xrgb8888);
        using var targetGuard = new BufferGuard(target);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        stage.Render(pass, texture!, new PostContext(32, 32, default));
        Assert.True(pass.Submit());

        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(target);
        var index = ((16 * 32) + 16) * 4;
        return (rgba[index], rgba[index + 1], rgba[index + 2]);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_matrix_that_drops_red_and_halves_blue_applies_in_linear_light(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(CtmShader.Source, CtmShader.Uniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the CTM shader");

        var stage = new CtmStage(shader);
        stage.SetMatrix([0, 0, 0, 0, 1, 0, 0, 0, 0.5]);
        Assert.False(stage.IsIdentity);
        var (r, g, b) = Through(host, stage, new RenderColor(0.8f, 0.6f, 1f, 1f));

        Assert.True(r <= 2, $"red {r} should be dropped");
        Assert.True(Math.Abs(g - 153) <= 3, $"green {g} should pass through as 153");
        Assert.True(Math.Abs(b - 188) <= 4, $"blue {b} should halve in linear light to about 188");
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void The_identity_passes_the_frame_through(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(CtmShader.Source, CtmShader.Uniforms);

        var stage = new CtmStage(shader);
        Assert.True(stage.IsIdentity);
        var (r, g, b) = Through(host, stage, new RenderColor(0.8f, 0.2f, 0.4f, 1f));
        Assert.True(r > 190 && g > 40 && g < 70 && b > 90 && b < 120, $"passed through as {r},{g},{b}");

        stage.SetMatrix([0, 0, 0, 0, 1, 0, 0, 0, 1]);
        stage.Reset();
        Assert.True(stage.IsIdentity);
    }
}
