using System.Runtime.CompilerServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Effects;
using Basin.Renderers;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class BackdropGlassTests
{
    private const int Width = 128;
    private const int Height = 96;
    private static readonly Box Node = new(16, 16, 96, 64);

    public static TheoryData<string> GlassRows => ["gl", "vulkan", "skia-gl", "skia-vulkan", "skia-graphite"];

    public static TheoryData<string, GlassPass> PassRows => new()
    {
        { "gl", GlassPass.Composite },
        { "gl", GlassPass.Fused },
        { "vulkan", GlassPass.Composite },
        { "vulkan", GlassPass.Fused },
    };

    public static TheoryData<string> NoGlassRows => ["pixman", "skia"];

    private static readonly FrameBackdropShape Shape = new()
    {
        Width = Node.Width,
        Height = Node.Height,
        CornerRadius = 12,
        EdgeWidth = 10,
        ClientHole = new Box(4, 30, Node.Width - 8, Node.Height - 34),
        HighlightX = 30,
        HighlightY = 12,
        HighlightStrength = 1,
        Emphasis = 1,
    };

    private static string ShaderDirectory([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "src", "Basin.Effects", "shaders"));

    [Theory]
    [InlineData("glass.frag", nameof(GlassShaders.Composite))]
    [InlineData("glass_fused_down.frag", nameof(GlassShaders.FusedDown))]
    [InlineData("glass_fused.frag", nameof(GlassShaders.Fused))]
    [InlineData("glass_copy.frag", nameof(GlassShaders.Copy))]
    [InlineData("glass_mesh.frag", nameof(GlassShaders.MeshComposite))]
    [InlineData("glass_mesh_fused.frag", nameof(GlassShaders.MeshFused))]
    public void The_checked_in_fragment_is_the_vulkan_header_and_the_shared_body(string file, string body)
    {
        var text = body switch
        {
            nameof(GlassShaders.Composite) => GlassShaders.Composite,
            nameof(GlassShaders.FusedDown) => GlassShaders.FusedDown,
            nameof(GlassShaders.Copy) => GlassShaders.Copy,
            nameof(GlassShaders.MeshComposite) => GlassShaders.VulkanMeshInput + "\n" + GlassShaders.MeshComposite,
            nameof(GlassShaders.MeshFused) => GlassShaders.VulkanMeshInput + "\n" + GlassShaders.MeshFused,
            _ => GlassShaders.Fused,
        };
        var frag = File.ReadAllText(Path.Combine(ShaderDirectory(), file));

        Assert.Equal(GlassShaders.VulkanHeader + "\n" + text + "\n", frag);
        Assert.Equal(GlassShaders.VulkanMeshVertex + "\n", File.ReadAllText(Path.Combine(ShaderDirectory(), "glass_mesh.vert")));
    }

    [Theory]
    [MemberData(nameof(PassRows))]
    public void A_deformed_glass_node_follows_its_mesh(string row, GlassPass pass)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var options = new GlassOptions { Pass = pass, Specular = 0, Noise = 0, Lens = 0, Tint = 0xC0FF0000, InactiveTint = 0xC0FF0000, ContentBorderWidth = 0 };
        var glass = Render(row, options, Shape with { HighlightStrength = 0 }, under: null, skew: 0.5);
        var plain = Render(row, null, Shape with { HighlightStrength = 0 }, under: null, skew: 0.5);
        Golden.AssertMatches(glass, Width, Height, $"glass-deformed-{(pass == GlassPass.Fused ? "fused" : "composite")}-{row}", tolerance: 2);

        var sheared = (((Node.Y + 4) * Width) + Node.X + 60) * 4;
        Assert.True(glass[sheared] > glass[sheared + 2] + 60, $"no glass inside the sheared shape: {glass[sheared]},{glass[sheared + 2]}");

        AssertPixel(plain, glass, Node.X + 4, Node.Y + 4);
        AssertPixel(plain, glass, Node.X + Node.Width + 24, Node.Y + 60);

        var slanted = (((Node.Y + 32) * Width) + Node.X + 22) * 4;
        Assert.True(glass[slanted] > glass[slanted + 2] + 60, "no glass along the slanted left edge");
    }

    [Fact]
    public void The_push_block_is_128_bytes()
    {
        Assert.Equal(128, Unsafe.SizeOf<GlassFrame>());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 16)]
    [InlineData(15, 32)]
    public void The_fused_downsample_keeps_the_working_sigma_small(int strength, int factor)
    {
        Assert.Equal(factor, GlassFrame.Factor(strength, out var sigma));
        Assert.InRange(sigma, 0.5, 3.0);
    }

    [Theory]
    [MemberData(nameof(NoGlassRows))]
    public void A_row_without_backdrops_gets_no_glass(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        try
        {
            Assert.Null(BackdropGlass.For(stack.Renderer));
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(GlassRows))]
    public void A_row_with_backdrops_gets_the_glass_for_its_device(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        try
        {
            using var glass = BackdropGlass.For(stack.Renderer);
            Assert.NotNull(glass);
            Assert.Equal(row is "gl" or "skia-gl", glass is GlBackdropGlass);
            Assert.Equal(row is not ("gl" or "skia-gl"), glass is VulkanBackdropGlass);
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(PassRows))]
    public void Glass_samples_only_what_is_beneath_the_node(string row, GlassPass pass)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var options = new GlassOptions
        {
            Pass = pass,
            Strength = 15,
            Lens = 4,
            Tint = 0,
            InactiveTint = 0,
            Specular = 0,
            Noise = 0,
            Saturation = 1,
        };
        var rgba = Render(row, options, Shape with { EdgeWidth = 40, HighlightStrength = 0 }, under: 0xFF808080);
        for (var y = Node.Y; y < Node.Bottom; y++)
        {
            for (var x = Node.X; x < Node.Right; x++)
            {
                if (GlassField.Distance(x + 0.5 - Node.X, y + 0.5 - Node.Y, Node.Width, Node.Height, 12) > -1.5)
                {
                    continue;
                }

                var i = ((y * Width) + x) * 4;
                var r = rgba[i];
                var g = rgba[i + 1];
                Assert.True(Math.Abs(r - g) <= 2, $"red from outside the node at ({x},{y}) on {row} {pass}: r={r} g={g}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PassRows))]
    public void Glass_changes_the_backdrop_and_leaves_the_corners_alone(string row, GlassPass pass)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var glass = Render(row, new GlassOptions { Pass = pass }, Shape, under: null);
        var plain = Render(row, null, Shape, under: null);

        Assert.NotEqual(plain, glass);
        AssertPixel(plain, glass, Node.X, Node.Y);
        AssertPixel(plain, glass, Node.Right - 1, Node.Bottom - 1);
        AssertPixel(plain, glass, 4, 4);
    }

    [Theory]
    [MemberData(nameof(PassRows))]
    public void Emphasis_moves_the_tint(string row, GlassPass pass)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var options = new GlassOptions { Pass = pass, Tint = 0xC0FF0000, InactiveTint = 0xC00000FF, Specular = 0, Noise = 0 };
        var active = Render(row, options, Shape with { HighlightStrength = 0 }, under: null);
        var inactive = Render(row, options, Shape with { HighlightStrength = 0, Emphasis = 0 }, under: null);
        var i = (((Node.Y + 32) * Width) + Node.X + 48) * 4;
        Assert.True(active[i] > active[i + 2] + 60, $"active r={active[i]} b={active[i + 2]}");
        Assert.True(inactive[i + 2] > inactive[i] + 60, $"inactive r={inactive[i]} b={inactive[i + 2]}");
    }

    [Theory]
    [MemberData(nameof(PassRows))]
    public void The_glint_stays_out_of_the_client_hole(string row, GlassPass pass)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var options = new GlassOptions { Pass = pass, Specular = 0, Noise = 0, GlintRadius = 200 };
        var lit = Render(row, options, Shape with { HighlightY = 50 }, under: null);
        var dark = Render(row, options, Shape with { HighlightStrength = 0 }, under: null);
        var hole = (((Node.Y + 50) * Width) + Node.X + 30) * 4;
        var chrome = (((Node.Y + 12) * Width) + Node.X + 30) * 4;
        Assert.Equal(dark[hole], lit[hole]);
        Assert.True(lit[chrome] > dark[chrome] + 10, $"chrome lit={lit[chrome]} dark={dark[chrome]}");
    }

    [Theory]
    [MemberData(nameof(PassRows))]
    public void The_content_border_follows_the_rounded_hole(string row, GlassPass pass)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var bordered = new GlassOptions
        {
            Pass = pass, Specular = 0, Noise = 0, Tint = 0, InactiveTint = 0,
            ContentBorder = 0xFFFF00FF, InactiveContentBorder = 0xFFFF00FF, ContentBorderWidth = 2,
        };
        var shape = Shape with { HighlightStrength = 0, ClientRadius = 10 };
        var ring = Render(row, bordered, shape, under: null);
        var plain = Render(row, bordered with { ContentBorderWidth = 0 }, shape, under: null);
        var hole = new Box(Node.X + 4, Node.Y + 30, Node.Width - 8, Node.Height - 34);

        var edge = (((hole.Y - 1) * Width) + hole.X + 40) * 4;
        Assert.True(IsMagenta(ring, edge), $"no border above the hole: {ring[edge]},{ring[edge + 1]},{ring[edge + 2]}");
        Assert.False(IsMagenta(plain, edge));

        var inside = (((hole.Y + 12) * Width) + hole.X + 40) * 4;
        Assert.False(IsMagenta(ring, inside));

        var corner = ((hole.Y * Width) + hole.X) * 4;
        Assert.False(IsMagenta(ring, corner), "the border cuts the corner square instead of following the radius");
        var diagonal = (int)Math.Round(10 - (10 / Math.Sqrt(2)));
        var curve = (((hole.Y + diagonal - 1) * Width) + hole.X + diagonal - 1) * 4;
        Assert.True(IsMagenta(ring, curve), $"no border on the rounded corner: {ring[curve]},{ring[curve + 1]},{ring[curve + 2]}");
    }

    private static bool IsMagenta(byte[] rgba, int i) => rgba[i] > 150 && rgba[i + 2] > 150 && rgba[i + 1] < 100;

    [Theory]
    [MemberData(nameof(PassRows))]
    public void A_node_half_off_the_target_keeps_its_field(string row, GlassPass pass)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        var options = new GlassOptions { Pass = pass, Specular = 0, Noise = 0, Lens = 0 };
        var shifted = Render(row, options, Shape with { HighlightStrength = 0 }, under: null, nodeX: -48);
        var plain = Render(row, null, Shape with { HighlightStrength = 0 }, under: null, nodeX: -48);
        AssertPixel(plain, shifted, Node.Right - 48 - 1, Node.Bottom - 1);
        var edge = Node.Y * Width * 4;
        Assert.NotEqual(plain[edge], shifted[edge]);
    }

    public static TheoryData<string, GlassPass, bool> GoldenRows => new()
    {
        { "gl", GlassPass.Composite, true },
        { "gl", GlassPass.Composite, false },
        { "gl", GlassPass.Fused, true },
        { "gl", GlassPass.Fused, false },
        { "vulkan", GlassPass.Composite, true },
        { "vulkan", GlassPass.Composite, false },
        { "vulkan", GlassPass.Fused, true },
        { "vulkan", GlassPass.Fused, false },
    };

    [Theory]
    [MemberData(nameof(GoldenRows))]
    public void Golden_glass(string row, GlassPass pass, bool active)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipUnless(CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this device");
        var rgba = Render(row, new GlassOptions { Pass = pass }, Shape with { Emphasis = active ? 1 : 0 }, under: null);
        var name = $"glass-{(pass == GlassPass.Fused ? "fused" : "composite")}-{(active ? "active" : "inactive")}-{row}";
        Golden.AssertMatches(rgba, Width, Height, name, tolerance: 2);
    }

    private static void AssertPixel(byte[] expected, byte[] actual, int x, int y)
    {
        var i = ((y * Width) + x) * 4;
        for (var c = 0; c < 3; c++)
        {
            Assert.True(Math.Abs(expected[i + c] - actual[i + c]) <= 1, $"({x},{y}) channel {c}: {expected[i + c]} vs {actual[i + c]}");
        }
    }

    internal static byte[] Render(
        string row, GlassOptions? options, in FrameBackdropShape shape, uint? under, int nodeX = 16, double skew = 0)
    {
        var stack = RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
        var scene = new Scene.Scene();
        var floor = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        var veil = new MemoryBuffer(Node.Width, Node.Height, DrmFormat.Argb8888);
        var patch = under is null ? null : new MemoryBuffer(Node.Width, Node.Height, DrmFormat.Xrgb8888);
        var target = new MemoryBuffer(Width, Height, DrmFormat.Xrgb8888);
        using var region = new Pixman.PixmanRegion32();
        RoundedRegion.Fill(region, Node.Width, Node.Height, 12);
        var glass = options is null ? null : BackdropGlass.For(stack.Renderer);
        try
        {
            if (under is null)
            {
                Stripes(floor);
            }
            else
            {
                Fill(floor, 0xFFFF0000);
                Fill(patch!, under.Value);
            }

            Fill(veil, 0);
            var floorNode = new SceneBuffer(scene.Root);
            floorNode.SetBuffer(floor);
            SceneBuffer? patchNode = null;
            if (patch is not null)
            {
                patchNode = new SceneBuffer(scene.Root);
                patchNode.SetPosition(nodeX, Node.Y);
                patchNode.SetBuffer(patch);
            }

            SceneTree parent = skew == 0 ? scene.Root : new SceneTransform(scene.Root) { Deformer = new SkewDeformer(skew) };
            var node = new SceneBuffer(parent);
            node.SetPosition(nodeX, Node.Y);
            node.SetBuffer(veil);
            var key = new object();
            if (glass is not null)
            {
                glass.Options = options!.Value;
                glass.SetShape(key, shape);
                node.SetBackdropEffect(glass, region, key);
            }

            Assert.True(scene.Render(stack.Renderer, target, new RenderColor(0, 0, 0, 1)));
            var rgba = BufferCapture.ReadRgba(target);
            node.Destroy();
            patchNode?.Destroy();
            floorNode.Destroy();
            return rgba;
        }
        finally
        {
            glass?.Dispose();
            target.Destroy();
            patch?.Destroy();
            veil.Destroy();
            floor.Destroy();
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private sealed class SkewDeformer(double skew) : IMeshTransform
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

    private static unsafe void Stripes(MemoryBuffer buffer)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = (uint*)(view.Data + (y * view.Stride));
            for (var x = 0; x < buffer.Width; x++)
            {
                var band = ((x + y) / 8) % 3;
                row[x] = band switch { 0 => 0xFFE04030u, 1 => 0xFF30C060u, _ => 0xFF3050E0u };
            }
        }

        buffer.EndDataAccess();
    }

    private static unsafe void Fill(MemoryBuffer buffer, uint argb)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Write, out var view));
        for (var y = 0; y < buffer.Height; y++)
        {
            var row = (uint*)(view.Data + (y * view.Stride));
            for (var x = 0; x < buffer.Width; x++)
            {
                row[x] = argb;
            }
        }

        buffer.EndDataAccess();
    }
}
