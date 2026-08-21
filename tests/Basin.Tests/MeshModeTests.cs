using Basin.Scene;
using Xunit;

namespace Basin.Tests;

internal sealed class WaveDeformer : IMeshTransform
{
    public const int Cells = 4;

    public double Amplitude { get; set; } = 5;

    public Box MapBounds(in Box childBounds)
    {
        var reach = (int)Math.Ceiling(Amplitude);
        return new Box(childBounds.X - reach, childBounds.Y - reach, childBounds.Width + (2 * reach), childBounds.Height + (2 * reach));
    }

    public int VertexCount(in Box childBounds) => Cells * Cells * 6;

    public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
    {
        var write = 0;
        for (var j = 0; j < Cells; j++)
        {
            for (var i = 0; i < Cells; i++)
            {
                Span<(int I, int J)> corners = [(i, j), (i + 1, j), (i, j + 1), (i + 1, j), (i + 1, j + 1), (i, j + 1)];
                foreach (var (ci, cj) in corners)
                {
                    var fx = (double)ci / Cells;
                    var fy = (double)cj / Cells;
                    var u = childBounds.X + (fx * childBounds.Width);
                    var v = childBounds.Y + (fy * childBounds.Height);
                    var x = u;
                    var y = v + (Amplitude * Math.Sin(fx * Math.PI * 2));
                    into[write++] = new MeshVertex((float)x, (float)y, (float)u, (float)v, new RenderColor(1f, 1f, 1f, 1f));
                }
            }
        }
    }
}

public sealed class MeshModeTests
{
    [Fact]
    public void Zero_copy_path_serves_a_bare_buffer_child()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(40, 30, Fill.Gradient(40, 30));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var content = host.SurfaceScenes[0];
        var transform = new SceneTransform(host.Scene.Root);
        transform.SetPosition(20, 20);
        var node = FindBuffer(content.Tree);
        Assert.NotNull(node);
        node!.Reparent(transform);

        host.RenderFrame();
        var flat = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);

        transform.Deformer = new WaveDeformer { Amplitude = 0 };
        host.RenderFrame();
        var meshed = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        Assert.Equal(flat, meshed);

        transform.Deformer = new WaveDeformer { Amplitude = 6 };
        host.RenderFrame();
        var deformed = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        Assert.NotEqual(flat, deformed);
    }

    [Fact]
    public void Captured_path_serves_a_decorated_subtree_and_recaptures_on_damage()
    {
        using var host = new CompositorTestHost();
        var transform = new SceneTransform(host.Scene.Root);
        transform.SetPosition(20, 20);
        _ = new SceneRect(transform, 52, 42, new RenderColor(0.2f, 0.3f, 0.6f, 1f));
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(40, 30, Fill.Solid(40, 30, 0xFF804020));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(transform);
        content.Tree.SetPosition(6, 6);

        transform.Deformer = new WaveDeformer { Amplitude = 0 };
        host.RenderFrame();

        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int At(int x, int y) => (rgba[((y * host.Target.Width) + x) * 4] << 16)
            | (rgba[(((y * host.Target.Width) + x) * 4) + 1] << 8)
            | rgba[(((y * host.Target.Width) + x) * 4) + 2];
        Assert.Equal(0x804020, At(40, 40));

        Fill.Solid(40, 30, 0xFF206040)(buffer.Data, buffer.Stride);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 40, 30);
        surface.Commit();
        host.PumpToServer();
        host.RenderFrame();
        rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        Assert.Equal(0x206040, At(40, 40));
    }

    [Fact]
    public void Deformer_output_matches_between_oracle_and_scene_output()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var oracle = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var transform = new SceneTransform(host.Scene.Root);
        transform.SetPosition(30, 25);
        _ = new SceneRect(transform, 60, 46, new RenderColor(0.5f, 0.25f, 0.12f, 1f));
        var deformer = new WaveDeformer();
        transform.Deformer = deformer;

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!, "deformed rect");

        deformer.Amplitude = 8;
        transform.Deformer = null;
        transform.Deformer = deformer;
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, options));
        host.Scene.Render(host.Renderer, oracle, RenderColor.Black);
        AssertSamePixels(oracle, state.Buffer!, "amplitude change");

        oracle.Destroy();
    }

    [Fact]
    public void Scene_mesh_draws_additively_and_damages_its_bounds()
    {
        using var host = new CompositorTestHost();
        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0.25f, 0.25f, 0.25f, 1f));
        var mesh = new SceneMesh(host.Scene.Root);
        mesh.SetPosition(40, 40);
        mesh.Bounds = new Box(-20, -20, 60, 60);
        mesh.Blend = RenderBlend.Additive;
        mesh.Source = new TriangleSource();

        var boxes = new List<Box>();
        host.Scene.Damaged += (_, box) => boxes.Add(box);
        mesh.NotifyMeshChanged();
        Assert.Contains(boxes, box => box.X == 20 && box.Y == 20 && box.Width == 60 && box.Height == 60);

        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        int Red(int x, int y) => rgba[((y * host.Target.Width) + x) * 4];
        Assert.True(Red(40, 45) > Red(10, 10), "additive triangle brightens the background");

        var hit = host.Scene.NodeAt(40, 45);
        Assert.NotNull(hit);
        Assert.IsNotType<SceneMesh>(hit!.Value.Node);
    }

    private sealed class TriangleSource : IMeshSource
    {
        public int VertexCount(in Box bounds) => 3;

        public void WriteVertices(in Box bounds, Span<MeshVertex> into)
        {
            into[0] = new MeshVertex(-15, 15, 0, 0, new RenderColor(0.5f, 0f, 0f, 0.5f));
            into[1] = new MeshVertex(15, 15, 0, 0, new RenderColor(0.5f, 0f, 0f, 0.5f));
            into[2] = new MeshVertex(0, -15, 0, 0, new RenderColor(0.5f, 0f, 0f, 0.5f));
        }
    }

    private static SceneBuffer? FindBuffer(SceneNode node) => node switch
    {
        SceneBuffer buffer => buffer,
        SceneTree tree => tree.Children.Select(FindBuffer).FirstOrDefault(found => found is not null),
        _ => null,
    };

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
            expected.EndDataAccess();
            actual.EndDataAccess();
        }
    }
}
