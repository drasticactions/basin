using Xunit;

namespace Basin.Tests;

public sealed class GoldenTests
{
    public static TheoryData<string> Renderers => new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller" };

    private static void SkipWithoutGpu(string renderer) =>
        CompositorTestHost.SkipUnlessRunnable(renderer);

    private static string GoldenName(string name, string renderer) =>
        renderer == "pixman" ? name : $"{name}-{renderer}";

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_gradient_surface(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(128, 96, Fill.Gradient(128, 96));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 128, 96);
        surface.Commit();
        host.PumpToServer();

        host.SurfaceScenes[0].Tree.SetPosition(8, 6);
        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("gradient-surface", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_window_borders(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var full = new Basin.Scene.SceneTree(host.Scene.Root);
        full.SetPosition(16, 14);
        _ = new Basin.Scene.SceneRect(full, 44, 34, new RenderColor(0.15f, 0.5f, 0.25f, 1f));
        var fullBorders = new Basin.Shell.River.RiverBorders(full);
        fullBorders.Layout(
            Basin.Shell.Xdg.ResizeEdges.Top | Basin.Shell.Xdg.ResizeEdges.Bottom |
            Basin.Shell.Xdg.ResizeEdges.Left | Basin.Shell.Xdg.ResizeEdges.Right,
            5,
            Basin.Shell.River.RiverBorders.ToRenderColor(uint.MaxValue, 0x60000000, 0x20000000, uint.MaxValue),
            new Box(0, 0, 44, 34),
            visible: true);

        var partial = new Basin.Scene.SceneTree(host.Scene.Root);
        partial.SetPosition(90, 14);
        _ = new Basin.Scene.SceneRect(partial, 44, 34, new RenderColor(0.15f, 0.25f, 0.5f, 1f));
        var partialBorders = new Basin.Shell.River.RiverBorders(partial);
        partialBorders.Layout(
            Basin.Shell.Xdg.ResizeEdges.Left | Basin.Shell.Xdg.ResizeEdges.Bottom,
            5,
            Basin.Shell.River.RiverBorders.ToRenderColor(0x20000000, uint.MaxValue, 0x80000000, uint.MaxValue),
            new Box(0, 0, 44, 34),
            visible: true);

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("window-borders", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_clipped_window(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(20, 14);
        _ = new Basin.Scene.SceneRect(window, 112, 84, new RenderColor(0.48f, 0.64f, 0.97f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(100, 72, Fill.Gradient(100, 72));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 100, 72);
        surface.Commit();
        host.PumpToServer();

        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(window);
        content.Tree.SetPosition(6, 6);

        window.ClipBox = new Box(0, 0, 80, 60);

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("clipped-window", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_subsurface_stack(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var parent = host.Client.Compositor.CreateSurface();
        var parentBuffer = host.Client.CreateBuffer(100, 80, Fill.Solid(100, 80, 0xFFAA2222));
        parent.Attach(parentBuffer.Proxy, 0, 0);

        var above = host.Client.Compositor.CreateSurface();
        var aboveSub = host.Client.Subcompositor.GetSubsurface(above, parent);
        aboveSub.SetPosition(30, 20);
        var aboveBuffer = host.Client.CreateBuffer(40, 30, Fill.Solid(40, 30, 0xFF2222AA));
        above.Attach(aboveBuffer.Proxy, 0, 0);
        above.Commit();

        var below = host.Client.Compositor.CreateSurface();
        var belowSub = host.Client.Subcompositor.GetSubsurface(below, parent);
        belowSub.SetPosition(-10, 40);
        belowSub.PlaceBelow(parent);
        var belowBuffer = host.Client.CreateBuffer(60, 30, Fill.Solid(60, 30, 0xFF22AA22));
        below.Attach(belowBuffer.Proxy, 0, 0);
        below.Commit();

        parent.Commit();
        host.PumpToServer();

        host.SurfaceScenes[0].Tree.SetPosition(20, 10);
        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("subsurface-stack", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_fractional_scale_output(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(1.5)));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        host.SurfaceScenes[0].Tree.SetPosition(8, 6);
        var rect = new Scene.SceneRect(host.Scene.Root, 30, 20, new RenderColor(0.2f, 0.5f, 0.8f, 1f));
        rect.SetPosition(60, 40);

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("fractional-scale-output", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_viewport_crop_scale(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        var buffer = host.Client.CreateBuffer(64, 64, Fill.Gradient(64, 64));
        viewport.SetSource(Wayland.WlFixed.FromInt(16), Wayland.WlFixed.FromInt(16), Wayland.WlFixed.FromInt(32), Wayland.WlFixed.FromInt(32));
        viewport.SetDestination(96, 64);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.SurfaceScenes[0].Tree.SetPosition(10, 10);
        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("viewport-crop-scale", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_fractional_viewport_source(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        var buffer = host.Client.CreateBuffer(64, 64, Fill.Gradient(64, 64));
        viewport.SetSource(
            Wayland.WlFixed.FromDouble(8.5),
            Wayland.WlFixed.FromDouble(4.25),
            Wayland.WlFixed.FromDouble(39.5),
            Wayland.WlFixed.FromDouble(30.75));
        viewport.SetDestination(96, 64);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.SurfaceScenes[0].Tree.SetPosition(10, 10);
        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("fractional-viewport-source", renderer));
    }

    private static MemoryBuffer TransformTarget(CompositorTestHost host, in RenderTransform transform)
    {
        var source = new MemoryBuffer(64, 48, DrmFormat.Argb8888);
        Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
        Fill.Gradient(64, 48)(view.Data, view.Stride);
        source.EndDataAccess();
        var texture = host.Renderer.ImportTexture(source);
        Assert.NotNull(texture);

        var target = new MemoryBuffer(128, 96, DrmFormat.Xrgb8888);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.12f, 0.12f, 0.14f, 1f), new Box(0, 0, 128, 96));
        pass.AddTexture(texture!, new TextureRenderOptions
        {
            DstBox = new Box(32, 24, 64, 48),
            Transform = transform,
        });
        Assert.True(pass.Submit());
        texture!.Dispose();
        source.Destroy();
        return target;
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_transform_rotate(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var target = TransformTarget(host, RenderTransform.RotationAbout(Math.PI / 6, 64, 48));
        using var guard = new DeferDestroy(target);
        Golden.AssertMatches(target, GoldenName("transform-rotate", renderer), renderer == "pixman" ? 0 : 1);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_transform_perspective(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var perspective = RenderTransform.Multiply(
            RenderTransform.Translation(64, 48),
            RenderTransform.Multiply(
                new RenderTransform(1, 0, 0, 0, 1, 0, 0.004, 0.001, 1),
                RenderTransform.Translation(-64, -48)));
        var target = TransformTarget(host, perspective);
        using var guard = new DeferDestroy(target);
        Golden.AssertMatches(target, GoldenName("transform-perspective", renderer), renderer == "pixman" ? 0 : 1);
    }

    private static MeshVertex[] GridMesh(Box bounds, int textureWidth, int textureHeight, int cells, double amplitude)
    {
        var mesh = new MeshVertex[cells * cells * 6];
        var write = 0;
        for (var j = 0; j < cells; j++)
        {
            for (var i = 0; i < cells; i++)
            {
                Span<(int I, int J)> corners = [(i, j), (i + 1, j), (i, j + 1), (i + 1, j), (i + 1, j + 1), (i, j + 1)];
                foreach (var (ci, cj) in corners)
                {
                    var fx = (double)ci / cells;
                    var fy = (double)cj / cells;
                    var x = (float)(bounds.X + (fx * bounds.Width));
                    var y = (float)(bounds.Y + (fy * bounds.Height) + (amplitude * Math.Sin(fx * Math.PI * 2)));
                    mesh[write++] = new MeshVertex(
                        x, y, (float)(fx * textureWidth), (float)(fy * textureHeight), new RenderColor(1f, 1f, 1f, 1f));
                }
            }
        }

        return mesh;
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_mesh_textured(string renderer)
    {
        SkipWithoutGpu(renderer);
        Assert.SkipWhen(
            !CompositorTestHost.GoldensComparable(renderer),
            $"{renderer} samples this mesh differently on this driver");
        using var host = new CompositorTestHost(renderer: renderer);
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
        pass.AddMesh(texture, GridMesh(new Box(24, 20, 80, 48), 64, 48, 4, 6), new MeshRenderOptions());
        Assert.True(pass.Submit());
        texture!.Dispose();
        Golden.AssertMatches(target, GoldenName("mesh-textured", renderer), renderer == "pixman" ? 0 : 1);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_mesh_gouraud(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var target = new MemoryBuffer(128, 96, DrmFormat.Xrgb8888);
        using var guard = new DeferDestroy(target);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.12f, 0.12f, 0.14f, 1f), new Box(0, 0, 128, 96));
        Span<MeshVertex> triangles =
        [
            new(16, 76, 0, 0, new RenderColor(1f, 0f, 0f, 1f)),
            new(60, 12, 0, 0, new RenderColor(0f, 1f, 0f, 1f)),
            new(104, 76, 0, 0, new RenderColor(0f, 0f, 1f, 1f)),
            new(70, 60, 0, 0, new RenderColor(0.5f, 0.5f, 0f, 0.5f)),
            new(120, 20, 0, 0, new RenderColor(0f, 0.5f, 0.5f, 0.5f)),
            new(124, 88, 0, 0, new RenderColor(0.5f, 0f, 0.5f, 0.5f)),
        ];
        pass.AddMesh(null, triangles, new MeshRenderOptions());
        Assert.True(pass.Submit());
        Golden.AssertMatches(target, GoldenName("mesh-gouraud", renderer), renderer == "pixman" ? 0 : 1);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_mesh_additive(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var target = new MemoryBuffer(128, 96, DrmFormat.Xrgb8888);
        using var guard = new DeferDestroy(target);
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddRect(new RenderColor(0.25f, 0.25f, 0.25f, 1f), new Box(0, 0, 128, 96));
        Span<MeshVertex> triangles =
        [
            new(20, 80, 0, 0, new RenderColor(0.6f, 0f, 0f, 0.6f)),
            new(56, 16, 0, 0, new RenderColor(0.6f, 0f, 0f, 0.6f)),
            new(92, 80, 0, 0, new RenderColor(0.6f, 0f, 0f, 0.6f)),
            new(36, 80, 0, 0, new RenderColor(0f, 0.6f, 0f, 0.6f)),
            new(72, 16, 0, 0, new RenderColor(0f, 0.6f, 0f, 0.6f)),
            new(108, 80, 0, 0, new RenderColor(0f, 0.6f, 0f, 0.6f)),
        ];
        pass.AddMesh(null, triangles, new MeshRenderOptions { Blend = RenderBlend.Additive });
        Assert.True(pass.Submit());
        Golden.AssertMatches(target, GoldenName("mesh-additive", renderer), renderer == "pixman" ? 0 : 1);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_scene_transform(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var transform = new Basin.Scene.SceneTransform(host.Scene.Root);
        transform.SetPosition(30, 20);
        _ = new Basin.Scene.SceneRect(transform, 76, 60, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();

        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(transform);
        content.Tree.SetPosition(6, 6);

        transform.Matrix = RenderTransform.RotationAbout(Math.PI / 8, 38, 30);
        transform.Alpha = 0.9f;

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("scene-transform", renderer), gpuTolerance: 2);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_deformed_window(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);

        var transform = new Basin.Scene.SceneTransform(host.Scene.Root);
        transform.SetPosition(30, 24);
        _ = new Basin.Scene.SceneRect(transform, 76, 60, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(transform);
        content.Tree.SetPosition(6, 6);

        transform.Deformer = new WaveDeformer { Amplitude = 6 };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("deformed-window", renderer));
    }

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }
}
