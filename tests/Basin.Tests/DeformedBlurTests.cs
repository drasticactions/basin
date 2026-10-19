using Basin.Diagnostics;
using Basin.Effects;
using Basin.Renderers;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class DeformedBlurTests
{
    private const int Width = 160;
    private const int Height = 96;

    public static TheoryData<string> Rows => ["gl", "vulkan", "skia-gl", "skia-vulkan"];

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_deformed_blur_stays_inside_its_mesh(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var blurred = Render(row, blur: true, regionHeight: 64);
        var plain = Render(row, blur: false, regionHeight: 64);

        Assert.NotEqual(Pixel(plain, 80, 50), Pixel(blurred, 80, 50));
        Assert.Equal(Pixel(plain, 20, 20), Pixel(blurred, 20, 20));
        Assert.Equal(Pixel(plain, 140, 76), Pixel(blurred, 140, 76));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_deformed_blur_keeps_to_its_region(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var blurred = Render(row, blur: true, regionHeight: 20);
        var plain = Render(row, blur: false, regionHeight: 20);

        Assert.NotEqual(Pixel(plain, 80, 24), Pixel(blurred, 80, 24));
        Assert.Equal(Pixel(plain, 70, 60), Pixel(blurred, 70, 60));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_recaptured_translucent_node_does_not_build_up(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var shade = new MemoryBuffer(96, 64, DrmFormat.Argb8888);
        var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        try
        {
            Fill(shade, static (_, _) => 0x40000000u);
            var floor = new SceneRect(scene.Root, Width, Height, new RenderColor(1f, 1f, 1f, 1f));
            var transform = new SceneTransform(scene.Root) { Deformer = new Skew(0.5) };
            var node = new SceneBuffer(transform);
            node.SetPosition(16, 16);
            node.SetBuffer(shade);
            var other = new SceneRect(transform, 4, 4, new RenderColor(0f, 0f, 0f, 0f));
            other.SetPosition(16, 16);

            uint first = 0;
            for (var frame = 0; frame < 5; frame++)
            {
                node.NotifyContentChanged();
                Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
                var pixel = Pixel(BufferCapture.ReadRgba(target), 70, 50);
                if (frame == 0)
                {
                    first = pixel;
                }

                Assert.True(pixel == first, $"frame {frame}: {pixel:X6} after {first:X6} on {row}");
            }

            transform.Destroy();
            floor.Destroy();
        }
        finally
        {
            target.Destroy();
            shade.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    public static TheoryData<string> AllRows => ["pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite"];

    [Theory]
    [MemberData(nameof(AllRows))]
    public void A_captured_translucent_node_matches_the_same_node_drawn_directly(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var direct = RenderTranslucent(row, deformed: false);
        var captured = RenderTranslucent(row, deformed: true);
        var worst = 0;
        for (var i = 0; i < direct.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(direct[i] - captured[i]));
        }

        Assert.True(worst <= 3, $"the capture changes a translucent node by {worst} on {row}: direct {Pixel(direct, 40, 40):X6}, captured {Pixel(captured, 40, 40):X6}");
    }

    private static byte[] RenderTranslucent(string row, bool deformed)
    {
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var shade = new MemoryBuffer(64, 48, DrmFormat.Argb8888);
        var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        try
        {
            Fill(shade, static (x, _) => x < 32 ? 0x48484848u : 0xC0B0B0B0u);
            var floor = new SceneRect(scene.Root, Width, Height, new RenderColor(0.15f, 0.17f, 0.2f, 1f));
            SceneTree parent = deformed ? new SceneTransform(scene.Root) { Deformer = new Skew(0) } : scene.Root;
            var node = new SceneBuffer(parent);
            node.SetPosition(16, 16);
            node.SetBuffer(shade);
            if (deformed)
            {
                _ = new SceneRect(parent, 1, 1, new RenderColor(0f, 0f, 0f, 0f));
            }

            Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
            var rgba = BufferCapture.ReadRgba(target);
            if (parent != scene.Root)
            {
                parent.Destroy();
            }
            else
            {
                node.Destroy();
            }

            floor.Destroy();
            return rgba;
        }
        finally
        {
            target.Destroy();
            shade.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static uint Pixel(byte[] rgba, int x, int y)
    {
        var i = ((y * Width) + x) * 4;
        return (uint)((rgba[i] << 16) | (rgba[i + 1] << 8) | rgba[i + 2]);
    }

    private static byte[] Render(string row, bool blur, int regionHeight)
    {
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var floor = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(96, 64, DrmFormat.Argb8888);
        var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        using var region = new Pixman.PixmanRegion32(0, 0, 96, (uint)regionHeight);
        var effect = blur ? BackdropBlurs.For(stack.Renderer) : null;
        try
        {
            Fill(floor, static (x, y) => ((x + y) / 6 % 3) switch { 0 => 0xFFE04030u, 1 => 0xFF30C060u, _ => 0xFF3050E0u });
            Fill(veil, static (_, _) => 0u);
            var floorNode = new SceneBuffer(scene.Root);
            floorNode.SetBuffer(floor);
            var transform = new SceneTransform(scene.Root) { Deformer = new Skew(0.5) };
            var node = new SceneBuffer(transform);
            node.SetPosition(16, 16);
            node.SetBuffer(veil);
            if (effect is not null)
            {
                effect.Options = effect.Options with { Strength = 6, NoiseStrength = 0, Saturation = 1 };
                node.SetBackdropEffect(effect, region);
            }

            Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
            var rgba = BufferCapture.ReadRgba(target);
            transform.Destroy();
            floorNode.Destroy();
            return rgba;
        }
        finally
        {
            effect?.Dispose();
            target.Destroy();
            veil.Destroy();
            floor.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static unsafe void Fill(MemoryBuffer buffer, Func<int, int, uint> color)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < buffer.Height; y++)
        {
            var line = (uint*)(view.Data + (y * view.Stride));
            for (var x = 0; x < buffer.Width; x++)
            {
                line[x] = color(x, y);
            }
        }

        buffer.EndDataAccess();
    }

    private sealed class Skew(double skew) : IMeshTransform
    {
        public Box MapBounds(in Box childBounds) =>
            new(childBounds.X, childBounds.Y, childBounds.Width + (int)Math.Ceiling(childBounds.Height * skew), childBounds.Height);

        public int VertexCount(in Box childBounds) => 6;

        public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
        {
            var white = new RenderColor(1f, 1f, 1f, 1f);
            float left = childBounds.X, top = childBounds.Y, right = childBounds.Right, bottom = childBounds.Bottom;
            var shift = (float)(childBounds.Height * skew);
            into[0] = new MeshVertex(left + shift, top, left, top, white);
            into[1] = new MeshVertex(right + shift, top, right, top, white);
            into[2] = new MeshVertex(left, bottom, left, bottom, white);
            into[3] = new MeshVertex(right + shift, top, right, top, white);
            into[4] = new MeshVertex(right, bottom, right, bottom, white);
            into[5] = new MeshVertex(left, bottom, left, bottom, white);
        }
    }
}
