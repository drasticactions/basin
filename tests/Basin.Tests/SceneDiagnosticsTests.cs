using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Scene;
using Basin.UI.Skia;
using Xunit;

namespace Basin.Tests;

public sealed class SceneDiagnosticsTests
{
    [Fact]
    public void The_tree_names_its_nodes_and_their_backdrops()
    {
        using var host = new CompositorTestHost();
        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        var window = new SceneTree(host.Scene.Root) { DebugName = "window probe" };
        var node = new SceneBuffer(window) { BackdropOnly = true, DestinationWidth = 20, DestinationHeight = 10, DebugName = "probe-underlay" };
        using var region = new Pixman.PixmanRegion32(0, 0, 20, 10);
        node.SetBackdropEffect(new ProbeEffect(), region);

        var nodes = diagnostics.Tree();
        var lines = nodes.Select(n => SceneDump.Format(n)).ToList();
        Assert.Contains(lines, line => line.Contains("tree \"window probe\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("buffer \"probe-underlay\"", StringComparison.Ordinal)
            && line.Contains("backdrop-only", StringComparison.Ordinal)
            && line.Contains("backdrop=ProbeEffect region=0,0,20x10", StringComparison.Ordinal));
        var underlay = Assert.Single(nodes, n => n.Name == "probe-underlay");
        Assert.Equal(("buffer", "tree/window probe/probe-underlay", 20, 10), (underlay.Kind, underlay.Path, underlay.Width, underlay.Height));
        Assert.Equal("tree/window probe/probe-underlay", SceneDump.PathOf(node));
        window.Destroy();
    }

    [Fact]
    public void A_tracked_frame_reports_what_it_drew_with_and_drops_out_when_disposed()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        var frame = new Frame(uiHost, new OutsetFrameRenderer(), host.Scene.Root);

        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();

        var info = Assert.Single(diagnostics.Frames());
        Assert.Equal(("frame", 1L, true), (info.Name, info.Draws, info.Active));
        Assert.Contains("draws=1 ", SceneDiagnostics.Describe("FRAME", info), StringComparison.Ordinal);

        frame.Dispose();
        Assert.Empty(diagnostics.Frames());
    }

    [Fact]
    public void A_frame_joins_its_scene_on_its_own_and_leaves_when_disposed()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var window = MappedToplevel.Map(host, host.Client, 60, 50, 0xFF0000FF);
        var detached = new SceneTree(null);
        var late = new Frame(uiHost, new OutsetFrameRenderer(), detached) { Window = window.ServerSurface };
        var frame = new Frame(uiHost, new OutsetFrameRenderer(), host.Scene.Root);
        Assert.Equal([frame], host.Scene.Frames);

        detached.Reparent(host.Scene.Root);
        late.Configure(new Box(0, 0, 64, 48), 1.0, new FrameState { Title = "t" });
        late.Commit();
        Assert.Equal([frame, late], host.Scene.Frames);

        var drawn = new List<Frame>();
        host.Scene.FrameDrawn += drawn.Add;
        frame.Configure(new Box(0, 0, 64, 48), 1.0, new FrameState { Title = "t" });
        frame.Commit();
        Assert.Equal([frame], drawn);
        Assert.Equal(1, frame.DrawCount);

        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        Assert.NotNull(diagnostics.LocateWindow(window.ServerSurface, 0));
        late.Dispose();
        frame.Dispose();
        Assert.Empty(host.Scene.Frames);
        Assert.Equal(diagnostics.LocateSurface(window.ServerSurface, 0)!(), diagnostics.LocateWindow(window.ServerSurface, 0)!());
        detached.Destroy();
    }

    [Fact]
    public void Watching_hooks_the_scene_only_while_a_watcher_lives_and_sends_a_stack_once_per_path()
    {
        using var host = new CompositorTestHost();
        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        Assert.False(host.Scene.HasDamageHandlers);
        Assert.False(host.Scene.HasFrameDrawnHandlers);

        var plain = new List<DamageInfo>();
        var stacked = new List<DamageInfo>();
        using (diagnostics.WatchDamage(stacks: false, plain.Add))
        using (diagnostics.WatchDamage(stacks: true, stacked.Add))
        using (diagnostics.WatchFrames(_ => { }))
        {
            Assert.True(host.Scene.HasDamageHandlers);
            Assert.True(host.Scene.HasFrameDrawnHandlers);
            var rect = new SceneRect(host.Scene.Root, 4, 4, new RenderColor(1f, 0f, 0f, 1f)) { DebugName = "probe" };
            rect.SetPosition(2, 2);
            rect.SetPosition(3, 3);
            Assert.NotEmpty(plain);
            Assert.All(plain, damage => Assert.Null(damage.Stack));
            var probe = stacked.Where(damage => damage.Path == "tree/probe").ToList();
            Assert.True(probe.Count >= 2);
            Assert.NotNull(probe[0].Stack);
            Assert.All(probe.Skip(1), damage => Assert.Null(damage.Stack));
            rect.Destroy();
        }

        Assert.False(host.Scene.HasDamageHandlers);
        Assert.False(host.Scene.HasFrameDrawnHandlers);
    }

    [Fact]
    public void Recording_without_an_output_driver_says_why()
    {
        using var host = new CompositorTestHost();
        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        Assert.Equal(
            "no presented outputs to record from",
            diagnostics.Record(new RecordRequest("/tmp/never.png", 2) { Region = new Box(0, 0, 4, 4) }, _ => { }));
    }

    [Fact]
    public void A_tracked_frame_lists_its_parts_in_scene_coordinates_by_surface()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        var window = MappedToplevel.Map(host, host.Client, 60, 50, 0xFF0000FF);
        var other = MappedToplevel.Map(host, host.Client, 60, 50, 0xFF00FF00);
        var parent = new SceneTree(host.Scene.Root);
        parent.SetPosition(100, 200);
        var frame = new Frame(uiHost, new OutsetFrameRenderer(), parent);
        frame.Window = window.ServerSurface;
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();

        var parts = new List<(FramePart Part, Box Box)>();
        Assert.True(diagnostics.WriteParts(window.ServerSurface, (part, box) => parts.Add((part, box))));
        Assert.Equal((FramePart.Title, new Box(136, 240, 72, 20)), parts[0]);
        Assert.Equal((FramePart.Border, new Box(136, 260, 72, 52)), parts[1]);
        Assert.False(diagnostics.WriteParts(other.ServerSurface, (_, _) => { }));
        var follow = diagnostics.LocateWindow(window.ServerSurface, 8)!;
        Assert.Equal(new Box(128, 232, 88, 88), follow());
        Assert.Equal(diagnostics.LocateSurface(other.ServerSurface, 8)!(), diagnostics.LocateWindow(other.ServerSurface, 8)!());
        Assert.NotNull(diagnostics.LocateSurface(other.ServerSurface, 8)!());
        frame.Dispose();
        parent.Destroy();
    }

    [Fact]
    public void Map_to_scene_goes_through_positions_and_matrices()
    {
        using var host = new CompositorTestHost();
        var outer = new SceneTree(host.Scene.Root);
        outer.SetPosition(100, 50);
        var transform = new SceneTransform(outer) { Matrix = RenderTransform.Scale(2, 2) };
        transform.SetPosition(10, 20);
        var inner = new SceneTree(transform);
        inner.SetPosition(5, 5);
        Assert.Equal(new Box(120, 80, 20, 10), inner.MapToScene(new Box(0, 0, 10, 5)));
        Assert.Equal(new Box(105, 55, 10, 5), outer.MapToScene(new Box(5, 5, 10, 5)));
        outer.Destroy();
    }

    [Fact]
    public void Parts_follow_a_transform_above_the_frame()
    {
        using var host = new CompositorTestHost();
        using var uiHost = new SkiaUIHost();
        var diagnostics = new SceneDiagnostics(host.Scene, host.Renderer, host.Loop, outputs: null);
        var window = MappedToplevel.Map(host, host.Client, 60, 50, 0xFF0000FF);
        var transform = new SceneTransform(host.Scene.Root) { Matrix = RenderTransform.Scale(0.5, 0.5) };
        var frame = new Frame(uiHost, new OutsetFrameRenderer(), transform);
        frame.Window = window.ServerSurface;
        frame.Configure(new Box(40, 60, 64, 48), 1.0, new FrameState { Title = "t", Active = true });
        frame.Commit();

        var parts = new List<(FramePart Part, Box Box)>();
        Assert.True(diagnostics.WriteParts(window.ServerSurface, (part, box) => parts.Add((part, box))));
        Assert.Equal((FramePart.Title, new Box(18, 20, 36, 10)), parts[0]);
        frame.Dispose();
        transform.Destroy();
    }

    [Fact]
    public void The_recorded_box_lands_on_the_content_of_a_rotated_output()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null)
        {
            HeadlessMode = new OutputMode(64, 48, 60_000),
            ConfiguredTransform = _ => OutputTransform.Rotate90,
            ContinuousRepaint = true,
        };
        driver.CreateInitialOutputs();
        _ = new SceneRect(scene.Root, 48, 64, new RenderColor(0f, 0f, 0f, 1f));
        var red = new SceneRect(scene.Root, 10, 5, new RenderColor(1f, 0f, 0f, 1f));
        red.SetPosition(4, 6);
        var view = driver.Views[0];
        Assert.Equal(OutputTransform.Rotate90, view.Output.Transform);
        var deadline = Environment.TickCount64 + 2000;
        while (view.Rendered < 2 && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(5);
        }

        var box = SceneDiagnostics.Physical(view.Scene!, new Box(4, 6, 10, 5));
        Assert.Equal((5, 10), (box.Width, box.Height));
        Assert.True(BufferCapture.TryReadRgba(view.LastPresentedBuffer!, renderer, out var rgba));
        var width = view.LastPresentedBuffer!.Width;
        for (var y = box.Y; y < box.Bottom; y++)
        {
            for (var x = box.X; x < box.Right; x++)
            {
                Assert.True(rgba[((y * width) + x) * 4] > 200 && rgba[(((y * width) + x) * 4) + 1] < 50, $"({x},{y}) is not red");
            }
        }

        Assert.True(rgba[((box.Y * width) + box.Right) * 4] < 50 || box.Right >= width);
        scene.Root.Destroy();
    }

    [Fact]
    public void A_recording_follows_its_target_scales_its_image_and_stops_at_the_timeout_when_idle()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null)
        {
            HeadlessMode = new OutputMode(64, 48, 60_000),
            ContinuousRepaint = true,
        };
        driver.CreateInitialOutputs();
        var diagnostics = new SceneDiagnostics(scene, renderer, host.Loop, driver);
        _ = new SceneRect(scene.Root, 64, 48, new RenderColor(0f, 0f, 0f, 1f));
        var red = new SceneRect(scene.Root, 8, 6, new RenderColor(1f, 0f, 0f, 1f));
        red.SetPosition(4, 4);
        var path = Path.Combine(Path.GetTempPath(), $"basin-record-{Environment.ProcessId}.png");
        RecordingResult? result = null;
        Assert.Null(diagnostics.Record(
            new RecordRequest(path, 3) { Follow = () => new Box(red.X, red.Y, 8, 6), MaxDimension = 4 },
            done => result = done));
        var deadline = Environment.TickCount64 + 3000;
        while (result is null && Environment.TickCount64 < deadline)
        {
            red.SetPosition(red.X + 2, 4);
            host.Loop.Dispatch(5);
        }

        try
        {
            Assert.NotNull(result);
            Assert.Equal((3, false, 8, 6), (result.Frames, result.TimedOut, result.Width, result.Height));
            Assert.Equal((4, 9), (result.ImageWidth, result.ImageHeight));
            Assert.True(File.Exists(path));
            Assert.All(result.PerFrame, frame => Assert.True(frame.Luma > 40, $"frame {frame.Index} missed the red rect"));
            Assert.True(result.PerFrame[2].At.X > result.PerFrame[0].At.X);

            driver.ContinuousRepaint = false;
            for (var i = 0; i < 20; i++)
            {
                host.Loop.Dispatch(5);
            }

            RecordingResult? idle = null;
            Assert.Null(diagnostics.Record(
                new RecordRequest(path, 5) { Region = new Box(0, 0, 8, 8), TimeoutMs = 50 }, done => idle = done));
            deadline = Environment.TickCount64 + 2000;
            while (idle is null && Environment.TickCount64 < deadline)
            {
                host.Loop.Dispatch(5);
            }

            Assert.NotNull(idle);
            Assert.True(idle.TimedOut);
            Assert.True(idle.Frames < 5);

            RecordingResult? now = null;
            Assert.Null(diagnostics.Record(
                new RecordRequest(string.Empty, 1) { Region = new Box(0, 0, 8, 8), Immediate = true, KeepPixels = true }, done => now = done));
            Assert.NotNull(now);
            Assert.Equal((1, 8 * 8 * 4), (now.Frames, now.Pixels!.Length));
        }
        finally
        {
            File.Delete(path);
            scene.Root.Destroy();
        }
    }

    [Fact]
    public void A_host_with_an_output_driver_seeds_diagnostics_and_a_consumer_registration_wins()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using (var early = new OutputDriver(host, scene, new OutputLayout(), renderer, null))
        {
            var services = host.CreateServices().Freeze();
            Assert.IsType<HostSceneDiagnostics>(services.Find<ISceneDiagnostics>());
            Assert.Same(early, host.Driver);
        }

        using var frozenFirst = BasinHost.Create(HostOptions.ForBackend("headless"));
        var frozen = frozenFirst.CreateServices().Freeze();
        var deferred = Assert.IsType<HostSceneDiagnostics>(frozen.Find<ISceneDiagnostics>());
        Assert.Equal("the compositor has no output driver yet", deferred.Record(new RecordRequest(string.Empty, 1), _ => { }));
        var afterScene = new Scene.Scene();
        using (var after = new OutputDriver(frozenFirst, afterScene, new OutputLayout(), renderer, null))
        {
            _ = new SceneRect(afterScene.Root, 4, 4, new RenderColor(1f, 0f, 0f, 1f)) { DebugName = "probe" };
            Assert.Contains(deferred.Tree(), node => node.Name == "probe");
            afterScene.Root.Destroy();
        }

        using var bare = BasinHost.Create(HostOptions.ForBackend("headless"));
        var late = bare.CreateServices();
        using var driver = new OutputDriver(bare, new Scene.Scene(), new OutputLayout(), renderer, null);
        var own = new SceneDiagnostics(driver.Scene, renderer, bare.Loop, driver);
        late.Use<ISceneDiagnostics>(own);
        Assert.Same(own, late.Freeze().Find<ISceneDiagnostics>());
    }

    [Fact]
    public void A_zoom_maps_a_box_by_its_scale_and_other_stages_keep_it()
    {
        var zoom = new Basin.Effects.ZoomStage(null, new Basin.Effects.ZoomOptions { InitialZoom = 2 });
        var context = new PostContext(100, 80, default);
        Assert.True(zoom.TryMapBox(new Box(10, 20, 30, 10), context, out var zoomed));
        Assert.Equal(new Box(20, 40, 60, 20), zoomed);
        Assert.True(new Basin.Effects.InvertStage(null).TryMapBox(new Box(1, 2, 3, 4), context, out var same));
        Assert.Equal(new Box(1, 2, 3, 4), same);
        Assert.False(((IPostStage)new OpaqueStage()).TryMapBox(new Box(1, 2, 3, 4), context, out _));
    }

    [Fact]
    public void A_recording_through_a_stage_that_cannot_map_marks_its_frames_unmapped()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null)
        {
            HeadlessMode = new OutputMode(64, 48, 60_000),
            ContinuousRepaint = true,
        };
        driver.CreateInitialOutputs();
        driver.Views[0].Scene!.AddPostStage(new OpaqueStage());
        var diagnostics = new SceneDiagnostics(scene, renderer, host.Loop, driver);
        _ = new SceneRect(scene.Root, 64, 48, new RenderColor(0f, 0f, 0f, 1f));
        RecordingResult? result = null;
        Assert.Null(diagnostics.Record(
            new RecordRequest(string.Empty, 2) { Follow = () => new Box(4, 4, 8, 8) }, done => result = done));
        var deadline = Environment.TickCount64 + 2000;
        while (result is null && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(5);
        }

        Assert.NotNull(result);
        Assert.All(result.PerFrame, frame => Assert.True(frame.Unmapped));
        scene.Root.Destroy();
    }

    private sealed class OpaqueStage : IPostStage
    {
        public void Render(IRenderPass pass, ITexture frame, in PostContext context) =>
            pass.AddTexture(frame, new TextureRenderOptions { DstBox = new Box(0, 0, context.Width, context.Height) });
    }

    private sealed class ProbeEffect : IBackdropEffect;
}
