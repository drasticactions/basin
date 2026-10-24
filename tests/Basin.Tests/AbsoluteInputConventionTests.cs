using Basin.Seat;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class AbsoluteInputConventionTests
{
    private static Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1 BindPointers(CompositorTestHost host)
    {
        Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_virtual_pointer_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1>(e.Name, 2);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static (Basin.Scene.SceneTransform Frame, Basin.Scene.SceneSurface Node) Place(CompositorTestHost host, Surface surface, double x, double y)
    {
        var frame = new Basin.Scene.SceneTransform(host.Scene.Root)
        {
            Matrix = new RenderTransform(1, 0, x, 0, 1, y, 0, 0, 1),
        };
        var node = new Basin.Scene.SceneSurface(frame, surface);
        foreach (var hosted in host.SurfaceScenes)
        {
            if (ReferenceEquals(hosted.Surface, surface))
            {
                hosted.Tree.Enabled = false;
            }
        }

        return (frame, node);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_default_sink_maps_a_virtual_pointer_extent_onto_the_layout(bool mapper)
    {
        using var host = new CompositorTestHost(1024, 768);
        var sink = new SeatInputSink(host.Seat, host.Layout);
        using var manager = new Basin.Desktop.VirtualPointerManager(host.Display, sink);
        var proxy = BindPointers(host);
        var window = MappedToplevel.Map(host, host.Client);
        var (frame, node) = Place(host, window.ServerSurface, 100, 100);
        var pointer = host.Client.Seat!.GetPointer();
        var motions = new List<(double X, double Y)>();
        pointer.Motion += (_, e) => motions.Add((e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble()));
        host.PumpToClient();

        if (mapper)
        {
            host.Seat.Pointer.MapToSurface = host.Scene.TryMapToSurface;
        }

        host.Seat.Pointer.NotifyMotionAt(1, window.ServerSurface, 0, 0, 100, 100);
        host.PumpUntil(() => motions.Count == 1);

        var virtualPointer = proxy.CreateVirtualPointer(host.Client.Seat);
        virtualPointer.MotionAbsolute(2, 512, 384, 1024, 768);
        virtualPointer.Frame();
        host.PumpUntil(() => motions.Count == 2);
        Assert.Equal((412.0, 284.0), motions[1]);

        virtualPointer.Motion(3, WlFixed.FromDouble(10), WlFixed.FromDouble(-4));
        virtualPointer.Frame();
        host.PumpUntil(() => motions.Count == 3);
        Assert.Equal((422.0, 280.0), motions[2]);

        host.Seat.Pointer.MapToSurface = null;
        virtualPointer.Dispose();
        node.Destroy();
        frame.Destroy();
        host.PumpToServer();
    }

    [Fact]
    public void An_output_bound_virtual_pointer_maps_onto_its_output()
    {
        using var host = new CompositorTestHost(1280, 720);
        var left = host.Backend.CreateOutput(new OutputMode(1280, 720, 60_000), manualFrameClock: true);
        var layout = new OutputLayout();
        layout.Add(left, -1280, 0);
        layout.Add(host.Output, 0, 0);
        var sink = new RecordingInputSink();
        using var manager = new Basin.Desktop.VirtualPointerManager(host.Display, sink, layout);
        var proxy = BindPointers(host);
        var registry = host.Client.Display.GetRegistry();
        var output = registry.Bind<Wayland.WlOutput>(host.OutputGlobal.NameFor(Assert.Single(host.Display.Clients)), 4);
        host.PumpToClient();

        var bound = proxy.CreateVirtualPointerWithOutput(host.Client.Seat, output);
        bound.MotionAbsolute(1, 640, 360, 1280, 720);
        var whole = proxy.CreateVirtualPointer(host.Client.Seat);
        whole.MotionAbsolute(2, 640, 360, 1280, 720);
        host.PumpUntil(() => sink.AbsoluteMotions.Count == 2);

        Assert.Equal((1920.0, 360.0, 2560.0, 720.0), (sink.AbsoluteMotions[0].X, sink.AbsoluteMotions[0].Y, sink.AbsoluteMotions[0].Width, sink.AbsoluteMotions[0].Height));
        Assert.Equal((640.0, 360.0, 1280.0, 720.0), (sink.AbsoluteMotions[1].X, sink.AbsoluteMotions[1].Y, sink.AbsoluteMotions[1].Width, sink.AbsoluteMotions[1].Height));
        bound.Dispose();
        whole.Dispose();
        output.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_default_sink_without_a_layout_declines_absolute_motion()
    {
        using var host = new CompositorTestHost();
        var sink = new SeatInputSink(host.Seat);
        var window = MappedToplevel.Map(host, host.Client);
        var pointer = host.Client.Seat!.GetPointer();
        var motions = 0;
        pointer.Motion += (_, _) => motions++;
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 1, 1);

        Assert.False(sink.PointerMotionAbsolute(1, 10, 10, 160, 120));
        Assert.False(sink.TouchDown(1, 0, 10, 10, 160, 120));
        host.PumpToClient();
        host.PumpToClient();
        Assert.Equal(0, motions);
    }

    [Fact]
    public void The_default_sink_with_no_focus_moves_nothing_and_says_so()
    {
        using var host = new CompositorTestHost();
        var sink = new SeatInputSink(host.Seat, host.Layout);
        Assert.False(sink.PointerMotionAbsolute(1, 10, 10, 160, 120));
        Assert.False(sink.PointerMotion(2, 3, 3));
    }

    [Fact]
    public void Touch_maps_the_extent_onto_a_layout_that_starts_left_of_zero()
    {
        using var host = new CompositorTestHost();
        var output = host.Backend.CreateOutput(new OutputMode(2000, 1000, 60_000), manualFrameClock: true);
        var layout = new OutputLayout();
        layout.Add(output, -1000, 0);
        var hits = new RecordingHitTester();
        _ = new TouchRouter(host.Seat.Touch) { HitTester = hits };
        var sink = new SeatInputSink(host.Seat, layout);

        Assert.True(sink.TouchDown(1, 0, 1, 0.5, 2, 1));
        Assert.Equal((0.0, 500.0), Assert.Single(hits.Points));
    }

    [Fact]
    public void Every_sink_reads_the_same_extent_on_a_layout_away_from_the_origin()
    {
        using var host = new CompositorTestHost();
        var output = host.Backend.CreateOutput(new OutputMode(1920, 1080, 60_000), manualFrameClock: true);
        var layout = new OutputLayout();
        layout.Add(output, 1920, 0);

        var scene = new Basin.Desktop.SceneSeatInput(host.Seat, host.Scene, layout);
        Assert.True(scene.PointerMotionAbsolute(1, 960, 540, 1920, 1080));
        Assert.Equal((2880.0, 540.0), (scene.Pointer.X, scene.Pointer.Y));

        var pointer = new LayoutPointer(layout);
        var cursor = new Basin.Desktop.CursorController(layout);
        var binder = new Basin.Seat.Backends.SeatBinder(host.Seat, layout, pointer, cursor);
        var injector = new Basin.Seat.Backends.SeatInjector(binder, host.Seat, layout, pointer) { Moved = _ => { } };
        var hook = new Basin.Seat.Backends.HookInputSink();
        hook.Route(injector, host.Seat);
        Assert.True(hook.PointerMotionAbsolute(1, 960, 540, 1920, 1080));
        Assert.Equal((2880.0, 540.0), (pointer.X, pointer.Y));
        cursor.Dispose();

        var hits = new RecordingHitTester();
        _ = new TouchRouter(host.Seat.Touch) { HitTester = hits };
        Assert.True(scene.TouchDown(2, 0, 960, 540, 1920, 1080));
        Assert.Equal((2880.0, 540.0), Assert.Single(hits.Points));
    }

    private sealed class RecordingHitTester : ITouchHitTester
    {
        public List<(double X, double Y)> Points { get; } = [];

        public bool TryHit(double layoutX, double layoutY, out TouchHit hit)
        {
            Points.Add((layoutX, layoutY));
            hit = default;
            return false;
        }

        public bool TryMap(object? token, double layoutX, double layoutY, out double localX, out double localY)
        {
            localX = layoutX;
            localY = layoutY;
            return false;
        }
    }
}
