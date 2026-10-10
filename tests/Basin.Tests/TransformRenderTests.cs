using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class RenderTransformTests
{
    [Fact]
    public void Identity_is_identity()
    {
        Assert.True(RenderTransform.Identity.IsIdentity);
        Assert.True(RenderTransform.Identity.IsAffine);
        var (x, y) = RenderTransform.Identity.Map(12.5, -3.25);
        Assert.Equal(12.5, x);
        Assert.Equal(-3.25, y);
    }

    [Fact]
    public void Invert_round_trips()
    {
        var transform = RenderTransform.Multiply(
            RenderTransform.RotationAbout(0.7, 40, 30),
            RenderTransform.Multiply(RenderTransform.Scale(1.5, 0.75), RenderTransform.Translation(12, -8)));
        Assert.True(transform.TryInvert(out var inverse));
        var (fx, fy) = transform.Map(17, 23);
        var (bx, by) = inverse.Map(fx, fy);
        Assert.Equal(17, bx, 9);
        Assert.Equal(23, by, 9);
    }

    [Fact]
    public void Degenerate_transform_does_not_invert()
    {
        var flat = RenderTransform.Scale(1, 0);
        Assert.False(flat.TryInvert(out _));
    }

    [Fact]
    public void Perspective_round_trips()
    {
        var perspective = RenderTransform.Multiply(
            RenderTransform.Translation(64, 48),
            RenderTransform.Multiply(
                new RenderTransform(1, 0, 0, 0, 1, 0, 0.002, 0.001, 1),
                RenderTransform.Translation(-64, -48)));
        Assert.False(perspective.IsAffine);
        Assert.True(perspective.TryInvert(out var inverse));
        var (fx, fy) = perspective.Map(30, 20);
        var (bx, by) = inverse.Map(fx, fy);
        Assert.Equal(30, bx, 6);
        Assert.Equal(20, by, 6);
    }

    [Fact]
    public void Bounds_cover_rotated_corners()
    {
        var quarter = new RenderTransform(0, -1, 0, 1, 0, 0, 0, 0, 1);
        Assert.True(quarter.TryMapBounds(new Box(0, 0, 10, 4), out var bounds));
        Assert.Equal(-4, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(4, bounds.Width);
        Assert.Equal(10, bounds.Height);
    }

    [Fact]
    public void Bounds_refuse_a_corner_behind_the_projection()
    {
        var behind = new RenderTransform(1, 0, 0, 0, 1, 0, -0.5, 0, 1);
        Assert.False(behind.TryMapBounds(new Box(0, 0, 10, 10), out _));
    }
}

public sealed class TransformRenderTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" };

    private static MemoryBuffer GradientSource(int width, int height)
    {
        var source = new MemoryBuffer(width, height, DrmFormat.Argb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
        Fill.Gradient(width, height)(view.Data, view.Stride);
        source.EndDataAccess();
        return source;
    }

    private static MemoryBuffer Strip(params uint[] colors)
    {
        var source = new MemoryBuffer(colors.Length, 1, DrmFormat.Argb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            var row = (uint*)view.Data;
            for (var i = 0; i < colors.Length; i++)
            {
                row[i] = colors[i];
            }
        }

        source.EndDataAccess();
        return source;
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void A_rotated_crop_samples_only_its_texels_and_keeps_its_edge(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var source = Strip(0xffff0000, 0xff00ff00, 0xff0000ff, 0xffffffff);
        using var sourceGuard = new DeferDestroy(source);
        var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);

        var target = new MemoryBuffer(96, 96, DrmFormat.Xrgb8888);
        using var targetGuard = new DeferDestroy(target);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.1f, 0.1f, 0.1f, 1f), new Box(0, 0, 96, 96));
        pass.AddTexture(texture!, new TextureRenderOptions
        {
            DstBox = new Box(0, 0, 8, 8),
            SrcBox = new FBox(0, 0, 3, 1),
            Transform = RenderTransform.RotationAbout(0.3, 4, 4),
        });
        pass.AddRect(new RenderColor(0.1f, 0.1f, 0.1f, 1f), new Box(0, 0, 96, 96));
        pass.AddTexture(texture!, new TextureRenderOptions
        {
            DstBox = new Box(28, 28, 40, 40),
            SrcBox = new FBox(1, 0, 1, 1),
            Transform = RenderTransform.RotationAbout(0.5, 48, 48),
        });
        Assert.True(pass.Submit());

        var pixels = BufferCapture.ReadRgba(target);
        texture!.Dispose();
        int R(int x, int y) => pixels[((y * 96) + x) * 4];
        int G(int x, int y) => pixels[(((y * 96) + x) * 4) + 1];
        int B(int x, int y) => pixels[(((y * 96) + x) * 4) + 2];
        Assert.True(G(48, 48) >= 240, $"{renderer}: center {R(48, 48)},{G(48, 48)},{B(48, 48)} top {R(48, 30)},{G(48, 30)},{B(48, 30)}");
        Assert.InRange(R(48, 48), 0, 30);
        Assert.True(G(48, 30) >= 128, $"{renderer}: inside the edge {R(48, 30)},{G(48, 30)},{B(48, 30)}");
        foreach (var (x, y) in new[] { (26, 26), (70, 70), (70, 26), (26, 70), (22, 48), (74, 48) })
        {
            Assert.True(R(x, y) <= 40 && G(x, y) <= 40 && B(x, y) <= 40, $"{renderer}: outside the quad at {x},{y}: {R(x, y)},{G(x, y)},{B(x, y)}");
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Translation_transform_matches_offset_draw(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var source = GradientSource(40, 30);
        using var sourceGuard = new DeferDestroy(source);
        var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);

        var transformed = new MemoryBuffer(96, 72, DrmFormat.Xrgb8888);
        using var transformedGuard = new DeferDestroy(transformed);
        var offset = new MemoryBuffer(96, 72, DrmFormat.Xrgb8888);
        using var offsetGuard = new DeferDestroy(offset);

        var pass = host.Renderer.BeginBufferPass(transformed, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.1f, 0.1f, 0.1f, 1f), new Box(0, 0, 96, 72));
        pass.AddTexture(texture!, new TextureRenderOptions
        {
            DstBox = new Box(10, 8, 40, 30),
            Transform = RenderTransform.Translation(7, 5),
        });
        Assert.True(pass.Submit());

        pass = host.Renderer.BeginBufferPass(offset, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.1f, 0.1f, 0.1f, 1f), new Box(0, 0, 96, 72));
        pass.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(17, 13, 40, 30) });
        Assert.True(pass.Submit());

        var actual = BufferCapture.ReadRgba(transformed);
        var expected = BufferCapture.ReadRgba(offset);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(
                Math.Abs(expected[i] - actual[i]) <= 1,
                $"byte {i}: expected {expected[i]}, got {actual[i]}");
        }

        texture!.Dispose();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Degenerate_transform_draws_nothing(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var source = GradientSource(40, 30);
        using var sourceGuard = new DeferDestroy(source);
        var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);

        var target = new MemoryBuffer(96, 72, DrmFormat.Xrgb8888);
        using var targetGuard = new DeferDestroy(target);

        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.2f, 0.3f, 0.4f, 1f), new Box(0, 0, 96, 72));
        pass.AddTexture(texture!, new TextureRenderOptions
        {
            DstBox = new Box(10, 8, 40, 30),
            Transform = RenderTransform.Scale(1, 0),
        });
        Assert.True(pass.Submit());

        var flat = BufferCapture.ReadRgba(target);
        var reference = new MemoryBuffer(96, 72, DrmFormat.Xrgb8888);
        using var referenceGuard = new DeferDestroy(reference);
        pass = host.Renderer.BeginBufferPass(reference, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.2f, 0.3f, 0.4f, 1f), new Box(0, 0, 96, 72));
        Assert.True(pass.Submit());
        var expected = BufferCapture.ReadRgba(reference);

        Assert.Equal(expected, flat);
        texture!.Dispose();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Shared_mesh_edge_is_watertight_under_additive(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        using var targetGuard = new DeferDestroy(target);

        var half = new RenderColor(0.4f, 0.4f, 0.4f, 0.4f);
        Span<MeshVertex> quad =
        [
            new(8, 8, 0, 0, half),
            new(56, 8, 0, 0, half),
            new(8, 56, 0, 0, half),
            new(56, 8, 0, 0, half),
            new(56, 56, 0, 0, half),
            new(8, 56, 0, 0, half),
        ];

        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.1f, 0.1f, 0.1f, 1f), new Box(0, 0, 64, 64));
        pass.AddMesh(null, quad, new MeshRenderOptions { Blend = RenderBlend.Additive });
        Assert.True(pass.Submit());

        var rgba = BufferCapture.ReadRgba(target);
        int At(int x, int y) => rgba[((y * 64) + x) * 4];
        var interior = At(20, 20);
        for (var i = 12; i < 52; i += 4)
        {
            Assert.True(
                Math.Abs(At(i, i) - interior) <= 1,
                $"diagonal pixel ({i},{i}) = {At(i, i)}, interior = {interior}");
        }
    }

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }
}
