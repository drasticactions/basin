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
    public void Golden_canvas_scale(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var map = ScaleCanvas(host);
        var scale = new Basin.Effects.CanvasScale { MinScale = 0.5 };
        using var theme = new TestFrameTheme();
        using var uiHost = new Basin.UI.Skia.SkiaUIHost();

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(36, 26, Fill.Gradient(36, 26));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 36, 26);
        surface.Commit();
        host.PumpToServer();

        var parked = new Box(0, 44, 36, 26);
        parked = parked with { X = scale.ParkTarget(map, map.Right!, parked) };
        var (right, rightFrame) = ScaledWindow(host, map, scale, parked, uiHost, theme, new RenderColor(0.2f, 0.3f, 0.6f, 1f));
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(right);
        content.Tree.SetPosition(0, 0);

        var corner = new Box(0, 0, 30, 20);
        var (cornerX, cornerY) = scale.CornerParkTarget(map, map.Left!, map.Top!, corner);
        var (_, cornerFrame) = ScaledWindow(host, map, scale, new Box(cornerX, cornerY, 30, 20), uiHost, theme, new RenderColor(0.7f, 0.35f, 0.2f, 1f));

        var menuOwner = new Box(0, 70, 34, 24);
        menuOwner = menuOwner with { X = scale.ParkTarget(map, map.Left!, menuOwner) };
        var (_, ownerFrame) = ScaledWindow(host, map, scale, menuOwner, uiHost, theme, new RenderColor(0.3f, 0.6f, 0.35f, 1f));
        var placement = scale.PlacementFor(map, menuOwner);
        var (originX, originY) = placement.Map(menuOwner.X, menuOwner.Y);
        var popup = new Basin.Scene.SceneTransform(host.Scene.Root)
        {
            Matrix = Basin.Effects.CanvasScale.About(placement.M11, 0, 0, Math.Round(originX), Math.Round(originY)),
        };
        var popupTree = new Basin.Scene.SceneTree(popup);
        popupTree.SetPosition(12, 14);
        _ = new Basin.Scene.SceneRect(popupTree, 26, 18, new RenderColor(0.95f, 0.95f, 0.9f, 1f));
        _ = new Basin.Scene.SceneRect(popupTree, 26, 6, new RenderColor(0.25f, 0.25f, 0.3f, 1f));

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-scale", renderer));
        rightFrame.Dispose();
        cornerFrame.Dispose();
        ownerFrame.Dispose();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_terrace(string renderer) => CanvasTerrace(renderer, 0.25, "canvas-terrace");

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_terrace_flat(string renderer) => CanvasTerrace(renderer, 0.0, "canvas-terrace-flat", separable: true);

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_terrace_plateau(string renderer) => CanvasTerrace(renderer, -0.4, "canvas-terrace-plateau");

    private static void CanvasTerrace(string renderer, double slope, string name, bool separable = false)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var left = new Basin.Effects.CanvasWarp();
        left.LayoutTerrace(30, -1, 14, 16, 0.4, Basin.Effects.CanvasWarp.TerraceExponent, slope, 60);
        var right = new Basin.Effects.CanvasWarp(1);
        right.LayoutTerrace(130, 1, 14, 16, 0.4, Basin.Effects.CanvasWarp.TerraceExponent, slope, 60);
        var top = new Basin.Effects.CanvasWarp();
        top.LayoutTerrace(22, -1, 10, 12, 0.5, Basin.Effects.CanvasWarp.TerraceExponent, slope, 80);
        var bottom = new Basin.Effects.CanvasWarp(1);
        bottom.LayoutTerrace(98, 1, 10, 12, 0.5, Basin.Effects.CanvasWarp.TerraceExponent, slope, 80);
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
                MinLineSpacing = 8,
                Separable = separable,
                Color = new RenderColor(0.16f, 0.21f, 0.75f, 1f),
            },
        };
        var map = new Basin.Effects.CanvasWarpTransform { Left = left, Right = right, Top = top, Bottom = bottom, Separable = separable };
        var scale = new Basin.Effects.CanvasScale();

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(36, 24, Fill.Gradient(36, 24));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 36, 24);
        surface.Commit();
        host.PumpToServer();
        var sloped = TerraceWindow(host, map, scale, new Box(112, 30, 36, 24), new RenderColor(0.2f, 0.3f, 0.6f, 1f));
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(sloped);
        content.Tree.SetPosition(0, 0);

        var resting = new Box(0, 58, 28, 20);
        resting = resting with { X = scale.TerraceParkTarget(map, left, resting, 0.2) };
        _ = TerraceWindow(host, map, scale, resting, new RenderColor(0.3f, 0.6f, 0.35f, 1f));

        var wide = new Box(0, 80, 100, 14);
        wide = wide with { X = scale.TerraceParkTarget(map, right, wide, 0.2) };
        _ = TerraceWindow(host, map, scale, wide, new RenderColor(0.7f, 0.35f, 0.2f, 1f));

        if (separable)
        {
            var corner = new Box(0, 0, 20, 14);
            var (cornerX, cornerY) = scale.TerraceCornerParkTarget(map, left, top, corner, 0.2);
            _ = TerraceWindow(host, map, scale, new Box(cornerX, cornerY, 20, 14), new RenderColor(0.8f, 0.7f, 0.2f, 1f));
        }

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName(name, renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_open(string renderer) => CanvasOverview(renderer, 1.0, "overview-open", fourSides: false, scale: 0.75);

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_open_corners(string renderer) => CanvasOverview(renderer, 1.0, "overview-open-corners", fourSides: true, scale: 0.6);

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_half(string renderer) => CanvasOverview(renderer, 0.5, "overview-half", fourSides: false, scale: 0.75);

    private static void CanvasOverview(string renderer, double progress, string name, bool fourSides, double scale)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        const double shelfScale = 0.4;
        var output = new Box(0, 0, 160, 120);
        var centerX = output.X + (output.Width / 2.0);
        var centerY = output.Y + (output.Height / 2.0);
        var zoom = 1.0 + ((scale - 1.0) * progress);
        var warps = new Basin.Effects.CanvasWarp[4];
        (int Outer, int Direction, double Center, double Fan, int Size)[] frames =
        [
            (output.X, -1, centerX, centerY, output.Width),
            (output.Right, 1, centerX, centerY, output.Width),
            (output.Y, -1, centerY, centerX, output.Height),
            (output.Bottom, 1, centerY, centerX, output.Height),
        ];
        for (var i = 0; i < 4; i++)
        {
            var (outer, direction, center, fan, size) = frames[i];
            warps[i] = new Basin.Effects.CanvasWarp(direction);
            var active = i < 2 || fourSides;
            var full = active
                ? TinyComp.OverviewLayout.Full(outer, outer, center, direction, scale, 0.1, size)
                : new TinyComp.OverviewSide(false, 0, 0, 0);
            var step = TinyComp.OverviewLayout.At(full, progress, outer, outer, center, direction, scale, shelfScale);
            warps[i].LayoutTerrace(outer, direction, step.Zone, step.Shelf, step.EdgeScale, Basin.Effects.CanvasWarp.TerraceExponent, 0.25, fan);
        }

        var wallpaper = new Basin.Scene.SceneTransform(host.Scene.Root)
        {
            Matrix = new RenderTransform(zoom, 0, centerX * (1.0 - zoom), 0, zoom, centerY * (1.0 - zoom), 0, 0, 1),
        };
        _ = new Basin.Scene.SceneRect(wallpaper, output.Width, output.Height, new RenderColor(0.12f, 0.2f, 0.16f, 1f));
        _ = new Basin.Scene.SceneMesh(host.Scene.Root)
        {
            Bounds = output,
            Source = new Basin.Effects.CanvasGridSource
            {
                Left = warps[0],
                Right = warps[1],
                Top = warps[2],
                Bottom = warps[3],
                CellSize = 16,
                MinLineSpacing = 8,
                ViewScale = zoom,
                ViewCenterX = centerX,
                ViewCenterY = centerY,
                Alpha = (float)progress,
                Color = new RenderColor(0.16f, 0.21f, 0.75f, 1f),
            },
        };
        var map = new Basin.Effects.CanvasWarpTransform
        {
            Left = warps[0],
            Right = warps[1],
            Top = warps[2],
            Bottom = warps[3],
            ViewScale = zoom,
            ViewCenterX = centerX,
            ViewCenterY = centerY,
        };
        var fit = new Basin.Effects.CanvasScale();

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(36, 24, Fill.Gradient(36, 24));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 36, 24);
        surface.Commit();
        host.PumpToServer();
        var desktop = TerraceWindow(host, map, fit, new Box(20, 20, 36, 24), new RenderColor(0.2f, 0.3f, 0.6f, 1f));
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(desktop);
        content.Tree.SetPosition(0, 0);
        _ = TerraceWindow(host, map, fit, new Box(70, 64, 50, 30), new RenderColor(0.3f, 0.6f, 0.35f, 1f));
        _ = TerraceWindow(host, map, fit, new Box(146, 30, 30, 20), new RenderColor(0.7f, 0.35f, 0.2f, 1f));

        var shelved = new Box(0, 70, 40, 26);
        shelved = shelved with { X = warps[0].IsIdentity ? shelved.X - 60 : fit.TerraceParkTarget(map, warps[0], shelved, 0.2) };
        _ = TerraceWindow(host, map, fit, shelved, new RenderColor(0.8f, 0.7f, 0.2f, 1f));

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName(name, renderer));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_step_open(string renderer) => CanvasOverviewStep(renderer, 1.0, "overview-step-open", fourSides: false);

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_step_corners(string renderer) => CanvasOverviewStep(renderer, 1.0, "overview-step-corners", fourSides: true);

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_step_half(string renderer) => CanvasOverviewStep(renderer, 0.5, "overview-step-half", fourSides: false);

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_step_textured(string renderer) => CanvasOverviewStep(
        renderer, 1.0, "overview-step-textured", fourSides: false,
        Basin.Effects.CanvasTexturePreset.Stone, Basin.Effects.CanvasTexturePreset.Wood);

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_step_textured_corners(string renderer) => CanvasOverviewStep(
        renderer, 1.0, "overview-step-textured-corners", fourSides: true,
        Basin.Effects.CanvasTexturePreset.Brick, Basin.Effects.CanvasTexturePreset.Noise);

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_overview_step_textured_half(string renderer) => CanvasOverviewStep(
        renderer, 0.5, "overview-step-textured-half", fourSides: false,
        Basin.Effects.CanvasTexturePreset.Stone, Basin.Effects.CanvasTexturePreset.Wood);

    private static void CanvasOverviewStep(
        string renderer, double progress, string name, bool fourSides,
        Basin.Effects.CanvasTexturePreset? wallTexture = null, Basin.Effects.CanvasTexturePreset? floorTexture = null)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        const double scale = 0.6;
        const double shelfScale = 0.3;
        const double wallWidth = 0.06;
        var output = new Box(0, 0, 160, 120);
        var centerX = output.X + (output.Width / 2.0);
        var centerY = output.Y + (output.Height / 2.0);
        var full = new TinyComp.OverviewSide[4];
        full[0] = TinyComp.OverviewLayout.StepFull(output.X, output.X, centerX, -1, scale, wallWidth, output.Width);
        full[1] = TinyComp.OverviewLayout.StepFull(output.Right, output.Right, centerX, 1, scale, wallWidth, output.Width);
        if (fourSides)
        {
            full[2] = TinyComp.OverviewLayout.StepFull(output.Y, output.Y, centerY, -1, scale, wallWidth, output.Height);
            full[3] = TinyComp.OverviewLayout.StepFull(output.Bottom, output.Bottom, centerY, 1, scale, wallWidth, output.Height);
        }

        var map = new Basin.Effects.CanvasStepMap();
        _ = TinyComp.OverviewLayout.LayoutStep(map, output, output, full, progress, scale, shelfScale);
        var zoom = map.Zoom;
        var wallpaper = new Basin.Scene.SceneTransform(host.Scene.Root)
        {
            Matrix = new RenderTransform(zoom, 0, centerX * (1.0 - zoom), 0, zoom, centerY * (1.0 - zoom), 0, 0, 1),
        };
        _ = new Basin.Scene.SceneRect(wallpaper, output.Width, output.Height, new RenderColor(0.12f, 0.2f, 0.16f, 1f));
        var wallBuffer = wallTexture is { } wallPreset ? Basin.Effects.CanvasTextures.Generate(wallPreset) : null;
        var floorBuffer = floorTexture is { } floorPreset ? Basin.Effects.CanvasTextures.Generate(floorPreset) : null;
        var walls = new Basin.Effects.CanvasStepSurfaceSource(Basin.Effects.CanvasStepSurface.Walls)
        {
            Map = map,
            TextureWidth = wallBuffer?.Width ?? 0,
            TextureHeight = wallBuffer?.Height ?? 0,
            TextureScale = 0.25,
        };
        var floor = new Basin.Effects.CanvasStepSurfaceSource(Basin.Effects.CanvasStepSurface.Floor)
        {
            Map = map,
            TextureWidth = floorBuffer?.Width ?? 0,
            TextureHeight = floorBuffer?.Height ?? 0,
            TextureScale = 0.25,
        };
        var floorMesh = new Basin.Scene.SceneMesh(host.Scene.Root) { Bounds = output, Source = floor };
        floorMesh.SetSpriteBuffer(floorBuffer);
        var wallMesh = new Basin.Scene.SceneMesh(host.Scene.Root) { Bounds = output, Source = walls };
        wallMesh.SetSpriteBuffer(wallBuffer);
        wallBuffer?.Destroy();
        floorBuffer?.Destroy();
        _ = new Basin.Scene.SceneMesh(host.Scene.Root)
        {
            Bounds = output,
            Source = new Basin.Effects.CanvasStepSource
            {
                Map = map,
                Walls = walls,
                CellSize = 16,
                MinLineSpacing = 4,
                Alpha = (float)progress,
                Color = new RenderColor(0.16f, 0.21f, 0.75f, 1f),
            },
        };

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(36, 24, Fill.Gradient(36, 24));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 36, 24);
        surface.Commit();
        host.PumpToServer();
        var desktop = StepWindow(host, map, Basin.Effects.CanvasStepPlane.Desktop, 1.0, new Box(20, 20, 36, 24), new RenderColor(0.2f, 0.3f, 0.6f, 1f));
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(desktop);
        content.Tree.SetPosition(0, 0);
        _ = StepWindow(host, map, Basin.Effects.CanvasStepPlane.Desktop, 1.0, new Box(70, 64, 50, 30), new RenderColor(0.3f, 0.6f, 0.35f, 1f));

        var end = new Basin.Effects.CanvasStepMap();
        _ = TinyComp.OverviewLayout.LayoutStep(end, output, output, full, 1.0, scale, shelfScale);
        var side = fourSides ? Basin.Effects.CanvasStepSides.Right | Basin.Effects.CanvasStepSides.Top : Basin.Effects.CanvasStepSides.Right;
        var strip = end.ShelfStrip(side);
        var shelved = new Box(0, 0, 40, 26);
        var fit = end.ShelfFit(shelved.Width, shelved.Height, side, 0.1);
        var drawnWidth = shelved.Width * shelfScale * fit;
        var drawnHeight = shelved.Height * shelfScale * fit;
        var drawnX = strip.Right - (drawnWidth / 2.0);
        var drawnY = fourSides ? strip.Y + (drawnHeight / 2.0) : centerY;
        var (canvasX, canvasY) = end.ToCanvas(Basin.Effects.CanvasStepPlane.Shelf, drawnX, drawnY);
        shelved = shelved with
        {
            X = (int)Math.Round(canvasX - (shelved.Width / 2.0)),
            Y = (int)Math.Round(canvasY - (shelved.Height / 2.0)),
        };
        _ = StepWindow(host, map, Basin.Effects.CanvasStepPlane.Shelf, 1.0 + ((fit - 1.0) * progress), shelved, new RenderColor(0.8f, 0.7f, 0.2f, 1f));

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName(name, renderer));
    }

    private static Basin.Scene.SceneTransform StepWindow(
        CompositorTestHost host,
        Basin.Effects.CanvasStepMap map,
        Basin.Effects.CanvasStepPlane plane,
        double fit,
        in Box box,
        RenderColor color)
    {
        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(box.X, box.Y);
        var placement = map.Placement(plane, fit, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
        var node = new Basin.Scene.SceneTransform(window)
        {
            Matrix = placement.IsIdentity ? RenderTransform.Identity : LocalPlacement(placement, box.X, box.Y),
        };
        _ = new Basin.Scene.SceneRect(node, box.Width, box.Height, color);
        return node;
    }

    private static Basin.Scene.SceneTransform TerraceWindow(
        CompositorTestHost host,
        Basin.Effects.CanvasWarpTransform map,
        Basin.Effects.CanvasScale scale,
        in Box box,
        RenderColor color)
    {
        var anchorX = box.X + (box.Width / 2.0);
        var anchorY = box.Y + (box.Height / 2.0);
        var transform = new Basin.Effects.CanvasWarpTransform
        {
            Left = map.Left,
            Right = map.Right,
            Top = map.Top,
            Bottom = map.Bottom,
            SceneX = box.X,
            SceneY = box.Y,
            CellSize = 4,
            PreScale = scale.SolveFit(map, box, 0.2, anchorX, anchorY),
            PreAnchorX = anchorX,
            PreAnchorY = anchorY,
            Separable = map.Separable,
        };
        if (map.Separable)
        {
            var (localX, localY) = map.LocalScale(anchorX, anchorY);
            var even = Math.Min(localX, localY);
            transform.PreStretchX = even / localX;
            transform.PreStretchY = even / localY;
        }

        transform.ViewScale = map.ViewScale;
        transform.ViewCenterX = map.ViewCenterX;
        transform.ViewCenterY = map.ViewCenterY;
        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(box.X, box.Y);
        var node = new Basin.Scene.SceneTransform(window);
        var local = new Box(0, 0, box.Width, box.Height);
        if (transform.IsPastFeet(local))
        {
            node.Matrix = LocalPlacement(transform.ShelfPlacement(local), box.X, box.Y);
        }
        else if (transform.ViewScale != 1.0 && transform.IsFlatFor(local))
        {
            node.Matrix = LocalPlacement(transform.ViewPlacement(), box.X, box.Y);
        }
        else
        {
            node.Deformer = transform;
        }

        _ = new Basin.Scene.SceneRect(node, box.Width, box.Height, color);
        return node;
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_scale_drag(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var map = ScaleCanvas(host);
        var scale = new Basin.Effects.CanvasScale { MinScale = 0.5 };
        using var theme = new TestFrameTheme();
        using var uiHost = new Basin.UI.Skia.SkiaUIHost();
        var box = new Box(map.Right!.Seam + 10, 40, 44, 32);
        var drag = scale.DragPlacementFor(map, box, box.X + 30.5, box.Y + 4.5, 128.25, 44.5);
        var (_, frame) = ScaledWindow(host, map, scale, box, uiHost, theme, new RenderColor(0.2f, 0.3f, 0.6f, 1f), drag);
        _ = new Basin.Scene.SceneRect(host.Scene.Root, 3, 3, new RenderColor(1f, 1f, 1f, 1f)) { };
        host.Scene.Root.Children[^1].SetPosition(127, 43);

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-scale-drag", renderer));
        frame.Dispose();
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_canvas_scale_over_a_deformer(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var map = ScaleCanvas(host);
        var scale = new Basin.Effects.CanvasScale { MinScale = 0.5 };
        var box = new Box(0, 30, 40, 30);
        box = box with { X = scale.ParkTarget(map, map.Right!, box) };
        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(box.X, box.Y);
        var canvasNode = new Basin.Scene.SceneTransform(window)
        {
            Matrix = LocalPlacement(scale.PlacementFor(map, box), box.X, box.Y),
        };
        var wobbly = new Basin.Scene.SceneTransform(canvasNode);
        _ = new Basin.Scene.SceneRect(wobbly, 40, 30, new RenderColor(0.2f, 0.3f, 0.6f, 1f));

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(32, 22, Fill.Gradient(32, 22));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 32, 22);
        surface.Commit();
        host.PumpToServer();
        var content = host.SurfaceScenes[0];
        content.Tree.Reparent(wobbly);
        content.Tree.SetPosition(4, 4);
        wobbly.Deformer = new WaveDeformer { Amplitude = 6 };

        host.RenderFrame();
        Golden.AssertMatches(host, GoldenName("canvas-scale-deformer", renderer));
    }

    private static Basin.Effects.CanvasWarpTransform ScaleCanvas(CompositorTestHost host)
    {
        var left = new Basin.Effects.CanvasWarp();
        left.Layout(24, -1, 24, 80, 0.2, 0.25, 60);
        var right = new Basin.Effects.CanvasWarp(1);
        right.Layout(136, 1, 24, 80, 0.2, 0.25, 60);
        var top = new Basin.Effects.CanvasWarp();
        top.Layout(18, -1, 18, 60, 0.2, 0.25, 80);
        var bottom = new Basin.Effects.CanvasWarp(1);
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
        return new Basin.Effects.CanvasWarpTransform { Left = left, Right = right, Top = top, Bottom = bottom };
    }

    private static (Basin.Scene.SceneTransform Node, Basin.Scene.Frame Frame) ScaledWindow(
        CompositorTestHost host,
        Basin.Effects.CanvasWarpTransform map,
        Basin.Effects.CanvasScale scale,
        in Box box,
        Basin.UI.Skia.SkiaUIHost uiHost,
        TestFrameTheme theme,
        RenderColor color,
        RenderTransform? placement = null)
    {
        var window = new Basin.Scene.SceneTree(host.Scene.Root);
        window.SetPosition(box.X, box.Y);
        var node = new Basin.Scene.SceneTransform(window)
        {
            Matrix = LocalPlacement(placement ?? scale.PlacementFor(map, box), box.X, box.Y),
        };
        var shadow = new Basin.Scene.SceneRect(node, box.Width, box.Height, new RenderColor(0f, 0f, 0f, 0.4f));
        shadow.SetPosition(3, 4);
        var frame = new Basin.Scene.Frame(uiHost, new TestFrameRenderer(theme), node);
        _ = new Basin.Scene.SceneRect(node, box.Width, box.Height, color);
        frame.Configure(new Box(0, 0, box.Width, box.Height), 1.0, new Basin.Capabilities.FrameState { Active = true });
        frame.Commit();
        return (node, frame);
    }

    private static RenderTransform LocalPlacement(in RenderTransform placement, int sceneX, int sceneY) =>
        RenderTransform.Multiply(
            RenderTransform.Translation(-sceneX, -sceneY),
            RenderTransform.Multiply(placement, RenderTransform.Translation(sceneX, sceneY)));

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
