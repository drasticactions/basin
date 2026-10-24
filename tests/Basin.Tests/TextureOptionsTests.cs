using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class TextureOptionsTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" };

    private static readonly RenderColor Background = new(0.1f, 0.1f, 0.1f, 1f);

    private static MemoryBuffer Solid(int width, int height, uint argb)
    {
        var buffer = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                new Span<uint>((void*)(view.Data + (y * view.Stride)), width).Fill(argb);
            }
        }

        buffer.EndDataAccess();
        return buffer;
    }

    private static byte[] Draw(CompositorTestHost host, MemoryBuffer source, MemoryBuffer target, params TextureRenderOptions[] draws)
    {
        var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(Background, new Box(0, 0, target.Width, target.Height));
        foreach (var draw in draws)
        {
            pass.AddTexture(texture!, draw);
        }

        Assert.True(pass.Submit());
        texture!.Dispose();
        return Basin.Diagnostics.BufferCapture.ReadRgba(target);
    }

    private static int Red(byte[] rgba, int width, int x, int y) => rgba[((y * width) + x) * 4];

    private static (int R, int G, int B) Pixel(byte[] rgba, int width, int x, int y)
    {
        var i = ((y * width) + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2]);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Corners_cut_the_draw_and_follow_an_outer_box(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var white = Solid(60, 60, 0xFFFFFFFFu);
        var target = new MemoryBuffer(200, 120, DrmFormat.Xrgb8888);
        try
        {
            var rgba = Draw(host, white, target,
                new TextureRenderOptions { DstBox = new Box(10, 10, 60, 60), Corners = new CornerClip(16) },
                new TextureRenderOptions { DstBox = new Box(100, 10, 60, 60), Corners = new CornerClip(24, 68, 94, 4, 30) });
            var background = Red(rgba, 200, 5, 5);
            Assert.True(Math.Abs(Red(rgba, 200, 10, 10) - background) <= 3, $"the bare corner is cut: {Red(rgba, 200, 10, 10)}");
            Assert.True(Red(rgba, 200, 40, 40) > 250, "the middle is drawn");
            Assert.True(Red(rgba, 200, 40, 10) > 250, "the edge between the corners is drawn");
            Assert.True(Red(rgba, 200, 100, 10) > 250, "a corner inside the outer box is drawn");
            Assert.True(Math.Abs(Red(rgba, 200, 100, 69) - background) <= 8, $"a corner on the outer curve is cut: {Red(rgba, 200, 100, 69)}");
        }
        finally
        {
            white.Destroy();
            target.Destroy();
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Brightness_scales_display_encoded_values_on_every_row(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var white = Solid(40, 40, 0xFFFFFFFFu);
        var target = new MemoryBuffer(60, 60, DrmFormat.Xrgb8888);
        try
        {
            var rgba = Draw(host, white, target, new TextureRenderOptions { DstBox = new Box(10, 10, 40, 40), Brightness = 0.75f });
            Assert.InRange(Red(rgba, 60, 30, 30), 188, 194);
        }
        finally
        {
            white.Destroy();
            target.Destroy();
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Saturation_zero_turns_a_color_into_its_luma(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var red = Solid(40, 40, 0xFFFF0000u);
        var target = new MemoryBuffer(60, 60, DrmFormat.Xrgb8888);
        try
        {
            var rgba = Draw(host, red, target, new TextureRenderOptions { DstBox = new Box(10, 10, 40, 40), Saturation = 0f });
            var (r, g, b) = Pixel(rgba, 60, 30, 30);
            Assert.True(Math.Abs(r - g) <= 2 && Math.Abs(g - b) <= 2, $"gray: {r},{g},{b}");
            Assert.InRange(r, 51, 57);
        }
        finally
        {
            red.Destroy();
            target.Destroy();
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Corners_and_brightness_compose_with_a_texture_shader(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var shader = host.Renderer.CompilePixelShader(PixelShaderTests.SplitSource, PixelShaderTests.SplitUniforms);
        Assert.SkipWhen(shader is null, $"{renderer} compiles no dialect of the test shader");
        shader.SetUniforms([0.5f]);
        var red = Solid(60, 60, 0xFFFF0000u);
        var target = new MemoryBuffer(80, 80, DrmFormat.Xrgb8888);
        try
        {
            var rgba = Draw(host, red, target, new TextureRenderOptions
            {
                DstBox = new Box(10, 10, 60, 60),
                Shader = shader,
                Corners = new CornerClip(16),
                Brightness = 0.75f,
            });
            var background = Red(rgba, 80, 5, 5);
            Assert.True(Math.Abs(Red(rgba, 80, 10, 10) - background) <= 3, $"the corner is cut: {Red(rgba, 80, 10, 10)}");
            var (r, g, b) = Pixel(rgba, 80, 20, 40);
            Assert.InRange(r, 188, 194);
            Assert.True(g < 10 && b < 10, $"the left half keeps the shader's color: {r},{g},{b}");
            var (gr, gg, gb) = Pixel(rgba, 80, 60, 40);
            Assert.True(Math.Abs(gr - gg) <= 2 && Math.Abs(gg - gb) <= 2 && gr < 60, $"the right half is the shader's gray, dimmed: {gr},{gg},{gb}");
        }
        finally
        {
            red.Destroy();
            target.Destroy();
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Corners_follow_a_transformed_draw(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var white = Solid(30, 30, 0xFFFFFFFFu);
        var target = new MemoryBuffer(120, 120, DrmFormat.Xrgb8888);
        try
        {
            var transform = RenderTransform.Multiply(RenderTransform.Translation(20, 20), RenderTransform.Scale(2, 2));
            var rgba = Draw(host, white, target, new TextureRenderOptions
            {
                DstBox = new Box(0, 0, 30, 30),
                Transform = transform,
                Corners = new CornerClip(10),
            });
            var (x0, y0) = transform.Map(0, 0);
            var (x1, y1) = transform.Map(30, 30);
            Assert.Equal(60, x1 - x0, 3);
            var background = Red(rgba, 120, 5, 5);
            var cornerX = (int)x0 + 1;
            var cornerY = (int)y0 + 1;
            var midX = (int)((x0 + x1) / 2);
            Assert.True(Math.Abs(Red(rgba, 120, cornerX, cornerY) - background) <= 3, $"the scaled corner is cut: {Red(rgba, 120, cornerX, cornerY)}");
            Assert.True(Red(rgba, 120, midX, (int)((y0 + y1) / 2)) > 250, "the middle is drawn");
            Assert.True(Red(rgba, 120, midX, cornerY) > 250, "the top edge is drawn");
        }
        finally
        {
            white.Destroy();
            target.Destroy();
        }
    }
}
