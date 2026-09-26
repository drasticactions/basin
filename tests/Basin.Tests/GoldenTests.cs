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

    private static (Basin.Effects.CanvasWarp Left, Basin.Effects.CanvasWarp Right) CanvasWarps()
    {
        var left = new Basin.Effects.CanvasWarp();
        left.Layout(seam: 24, direction: -1, zoneWidth: 24, extension: 80, edgeScale: 0.2);
        var right = new Basin.Effects.CanvasWarp();
        right.Layout(seam: 160 - 24, direction: 1, zoneWidth: 24, extension: 80, edgeScale: 0.2);
        return (left, right);
    }

    private static Basin.Scene.SceneMesh CanvasGrid(CompositorTestHost host, Basin.Effects.CanvasWarp left, Basin.Effects.CanvasWarp right) =>
        new(host.Scene.Root)
        {
            Bounds = new Box(0, 0, 160, 120),
            Source = new Basin.Effects.CanvasGridSource
            {
                Left = left,
                Right = right,
                CellSize = 16,
                Color = new RenderColor(0.16f, 0.21f, 0.75f, 1f),
            },
        };

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_parked_window(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var (left, right) = CanvasWarps();
        _ = CanvasGrid(host, left, right);

        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(left.FarEdge, 40);
        var canvasNode = new Basin.Scene.SceneTransform(window);
        using var theme = new TestFrameTheme();
        using var uiHost = new Basin.UI.Skia.SkiaUIHost();
        var frame = new Basin.Scene.Frame(uiHost, new TestFrameRenderer(theme), canvasNode);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(canvasNode);
        content.Tree.SetPosition(0, 0);
        frame.Configure(new Box(0, 0, 64, 48), 1.0, new Basin.Capabilities.FrameState { Active = true });
        frame.Commit();

        canvasNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            SceneX = window.X,
            CellSize = 8,
        };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-parked-window", renderer));
        frame.Dispose();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_straddling_window(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var (left, right) = CanvasWarps();
        _ = CanvasGrid(host, left, right);

        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(-20, 30);
        var canvasNode = new Basin.Scene.SceneTransform(window);
        _ = new Basin.Scene.SceneRect(canvasNode, 76, 60, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(canvasNode);
        content.Tree.SetPosition(6, 6);

        canvasNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            SceneX = window.X,
            CellSize = 8,
        };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-straddling-window", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_sloped_window(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var left = new Basin.Effects.CanvasWarp();
        left.Layout(seam: 32, direction: -1, zoneWidth: 32, extension: 100, edgeScale: 0.2, slope: 0.4, center: 60);
        var right = new Basin.Effects.CanvasWarp(1);
        right.Layout(seam: 160 - 32, direction: 1, zoneWidth: 32, extension: 100, edgeScale: 0.2, slope: 0.4, center: 60);
        _ = CanvasGrid(host, left, right);

        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(-30, 12);
        var canvasNode = new Basin.Scene.SceneTransform(window);
        _ = new Basin.Scene.SceneRect(canvasNode, 76, 40, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 28, Fill.Gradient(64, 28));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 28);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(canvasNode);
        content.Tree.SetPosition(6, 6);

        canvasNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            SceneX = window.X,
            SceneY = window.Y,
            CellSize = 8,
        };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-sloped-window", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_straddling_window_at_fractional_scale(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(1.5)));

        var left = new Basin.Effects.CanvasWarp();
        left.Layout(seam: 16, direction: -1, zoneWidth: 16, extension: 50, edgeScale: 0.2);
        var right = new Basin.Effects.CanvasWarp();
        right.Layout(seam: 107 - 16, direction: 1, zoneWidth: 16, extension: 50, edgeScale: 0.2);
        _ = new Basin.Scene.SceneMesh(host.Scene.Root)
        {
            Bounds = new Box(0, 0, 107, 80),
            Source = new Basin.Effects.CanvasGridSource
            {
                Left = left,
                Right = right,
                CellSize = 16,
                Color = new RenderColor(0.16f, 0.21f, 0.75f, 1f),
            },
        };

        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(-14, 20);
        var canvasNode = new Basin.Scene.SceneTransform(window);
        _ = new Basin.Scene.SceneRect(canvasNode, 60, 44, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(48, 32, Fill.Gradient(48, 32));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 48, 32);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(canvasNode);
        content.Tree.SetPosition(6, 6);

        canvasNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            SceneX = window.X,
            CellSize = 8,
        };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-straddling-window-scaled", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_four_sides(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var left = new Basin.Effects.CanvasWarp();
        left.Layout(24, -1, 24, 80, 0.2, 0.25, 60);
        var right = new Basin.Effects.CanvasWarp(1);
        right.Layout(136, 1, 24, 80, 0.2, 0.25, 60);
        var top = new Basin.Effects.CanvasWarp();
        top.Layout(18, -1, 18, 60, 0.2, 0.25, 80);
        var bottom = new Basin.Effects.CanvasWarp(1);
        bottom.Layout(102, 1, 18, 60, 0.2, 0.25, 80);
        _ = new Basin.Scene.SceneMesh(host.Scene.Root)
        {
            Bounds = new Box(0, 0, 160, 120),
            Source = new Basin.Effects.CanvasGridSource
            {
                Left = left,
                Right = right,
                Top = top,
                Bottom = bottom,
                CellSize = 16,
                Color = new RenderColor(0.16f, 0.21f, 0.75f, 1f),
            },
        };

        var corner = new Basin.Scene.SceneTree(host.Scene.Root);
        corner.SetPosition(
            (int)Math.Round(left.Seam - (left.Extension / Math.Sqrt(2))),
            (int)Math.Round(top.Seam - (top.Extension / Math.Sqrt(2))));
        var cornerNode = new Basin.Scene.SceneTransform(corner);
        _ = new Basin.Scene.SceneRect(cornerNode, 60, 44, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(48, 32, Fill.Gradient(48, 32));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 48, 32);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(cornerNode);
        content.Tree.SetPosition(6, 6);
        cornerNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
            SceneX = corner.X,
            SceneY = corner.Y,
            CellSize = 8,
        };

        var up = new Basin.Scene.SceneTree(host.Scene.Root);
        up.SetPosition(70, top.FarEdge);
        var upNode = new Basin.Scene.SceneTransform(up);
        _ = new Basin.Scene.SceneRect(upNode, 50, 40, new RenderColor(0.7f, 0.35f, 0.2f, 1f));
        _ = new Basin.Scene.SceneRect(upNode, 50, 8, new RenderColor(0.95f, 0.9f, 0.3f, 1f));
        upNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
            SceneX = up.X,
            SceneY = up.Y,
            CellSize = 8,
        };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-four-sides", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_four_sides_square(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var left = new Basin.Effects.CanvasWarp();
        left.Layout(24, -1, 24, 80, 0.2, 0.25, 60);
        var right = new Basin.Effects.CanvasWarp(1);
        right.Layout(136, 1, 24, 80, 0.2, 0.25, 60);
        var top = new Basin.Effects.CanvasWarp();
        top.Layout(18, -1, 18, 60, 0.2, 0.25, 80);
        var bottom = new Basin.Effects.CanvasWarp(1);
        bottom.Layout(102, 1, 18, 60, 0.2, 0.25, 80);
        _ = new Basin.Scene.SceneMesh(host.Scene.Root)
        {
            Bounds = new Box(0, 0, 160, 120),
            Source = new Basin.Effects.CanvasGridSource
            {
                Left = left,
                Right = right,
                Top = top,
                Bottom = bottom,
                CellSize = 16,
                CornerRadius = 0,
                Color = new RenderColor(0.16f, 0.21f, 0.75f, 1f),
            },
        };

        var corner = new Basin.Scene.SceneTree(host.Scene.Root);
        corner.SetPosition(left.FarEdge, top.FarEdge);
        var cornerNode = new Basin.Scene.SceneTransform(corner);
        _ = new Basin.Scene.SceneRect(cornerNode, 60, 44, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(48, 32, Fill.Gradient(48, 32));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 48, 32);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(cornerNode);
        content.Tree.SetPosition(6, 6);
        cornerNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
            SceneX = corner.X,
            SceneY = corner.Y,
            CellSize = 8,
            CornerRadius = 0,
        };

        var up = new Basin.Scene.SceneTree(host.Scene.Root);
        up.SetPosition(70, top.FarEdge);
        var upNode = new Basin.Scene.SceneTransform(up);
        _ = new Basin.Scene.SceneRect(upNode, 50, 40, new RenderColor(0.7f, 0.35f, 0.2f, 1f));
        _ = new Basin.Scene.SceneRect(upNode, 50, 8, new RenderColor(0.95f, 0.9f, 0.3f, 1f));
        upNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
            SceneX = up.X,
            SceneY = up.Y,
            CellSize = 8,
            CornerRadius = 0,
        };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-four-sides-square", renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_four_sides_taper(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var left = new Basin.Effects.CanvasWarp();
        left.Layout(24, -1, 24, 80, 0.2, 0.25, 60);
        var right = new Basin.Effects.CanvasWarp(1);
        right.Layout(136, 1, 24, 80, 0.2, 0.25, 60);
        var top = new Basin.Effects.CanvasWarp();
        top.Layout(18, -1, 18, 60, 0.2, 0.25, 80);
        var bottom = new Basin.Effects.CanvasWarp(1);
        bottom.Layout(102, 1, 18, 60, 0.2, 0.25, 80);
        _ = new Basin.Scene.SceneMesh(host.Scene.Root)
        {
            Bounds = new Box(0, 0, 160, 120),
            Source = new Basin.Effects.CanvasGridSource
            {
                Left = left,
                Right = right,
                Top = top,
                Bottom = bottom,
                CellSize = 16,
                CornerTaper = true,
                Color = new RenderColor(0.16f, 0.21f, 0.75f, 1f),
            },
        };

        var corner = new Basin.Scene.SceneTree(host.Scene.Root);
        corner.SetPosition(left.FarEdge, top.FarEdge);
        var cornerNode = new Basin.Scene.SceneTransform(corner);
        _ = new Basin.Scene.SceneRect(cornerNode, 60, 44, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(48, 32, Fill.Gradient(48, 32));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 48, 32);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(cornerNode);
        content.Tree.SetPosition(6, 6);
        cornerNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
            SceneX = corner.X,
            SceneY = corner.Y,
            CellSize = 8,
            CornerTaper = true,
        };

        var up = new Basin.Scene.SceneTree(host.Scene.Root);
        up.SetPosition(70, top.FarEdge);
        var upNode = new Basin.Scene.SceneTransform(up);
        _ = new Basin.Scene.SceneRect(upNode, 50, 40, new RenderColor(0.7f, 0.35f, 0.2f, 1f));
        _ = new Basin.Scene.SceneRect(upNode, 50, 8, new RenderColor(0.95f, 0.9f, 0.3f, 1f));
        upNode.Deformer = new Basin.Effects.CanvasWarpTransform
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
            SceneX = up.X,
            SceneY = up.Y,
            CellSize = 8,
            CornerTaper = true,
        };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-four-sides-taper", renderer));
    }

    private sealed class DeferDestroy(BufferBase buffer) : IDisposable
    {
        public void Dispose() => buffer.Destroy();
    }
}
