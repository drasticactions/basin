using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Plasma;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ScreencastTests
{
    private sealed class FakePublisher : IScreencastPublisher
    {
        public List<ScreencastRequest> Requests { get; } = [];

        public List<ulong> ClosedStreams { get; } = [];

        public uint NodeId { get; set; } = 77;

        public ulong ObjectSerial { get; set; }

        public string? FailureReason { get; set; }

        public bool Refuse { get; set; }

        public bool TryPublish(in ScreencastRequest request, out ScreencastStreamInfo info)
        {
            Requests.Add(request);
            info = new ScreencastStreamInfo
            {
                NodeId = NodeId,
                ObjectSerial = ObjectSerial,
                FailureReason = FailureReason,
            };
            return !Refuse;
        }

        public void Close(ulong streamId) => ClosedStreams.Add(streamId);
    }

    private sealed class FakeVirtualOutputs(CompositorTestHost host) : IVirtualOutputFactory
    {
        public List<(string Name, string Description, int Width, int Height, double Scale)> Created { get; } = [];

        public List<IOutput> Destroyed { get; } = [];

        public IOutput? LastOutput { get; private set; }

        public bool TryCreate(string name, string description, int width, int height,
                              double scale, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IOutput? output)
        {
            Created.Add((name, description, width, height, scale));
            var created = host.Backend.CreateOutput(new OutputMode(width, height, 60_000), manualFrameClock: true);
            LastOutput = created;
            output = created;
            return true;
        }

        public void Destroy(IOutput output)
        {
            Destroyed.Add(output);
            (output as Basin.Backend.Headless.HeadlessOutput)?.Destroy();
        }
    }

    private sealed class StreamEvents
    {
        public List<string> Order { get; } = [];

        public uint Node { get; private set; }

        public ulong Serial { get; private set; }

        public string? Error { get; private set; }

        public int Closed { get; private set; }

        public int Failed { get; private set; }

        public int Created { get; private set; }

        public static StreamEvents Attach(Basin.Plasma.Protocol.ZkdeScreencastStreamUnstableV1 stream)
        {
            var events = new StreamEvents();
            stream.Serial += (_, e) =>
            {
                events.Order.Add("serial");
                events.Serial = ((ulong)e.ObjectSerialHi << 32) | e.ObjectSerialLow;
            };
#pragma warning disable CS0618
            stream.Created += (_, e) =>
            {
                events.Order.Add("created");
                events.Created++;
                events.Node = e.Node;
            };
#pragma warning restore CS0618
            stream.Failed += (_, e) =>
            {
                events.Order.Add("failed");
                events.Failed++;
                events.Error = e.Error;
            };
            stream.Closed += (_, _) =>
            {
                events.Order.Add("closed");
                events.Closed++;
            };
            return events;
        }
    }

    private static Basin.Plasma.Protocol.ZkdeScreencastUnstableV1 Bind(CompositorTestHost host, uint version = 6)
    {
        Basin.Plasma.Protocol.ZkdeScreencastUnstableV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zkde_screencast_unstable_v1")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.ZkdeScreencastUnstableV1>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static void AssertClientAlive(CompositorTestHost host)
    {
        host.PumpToServer();
        host.PumpToClient();
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }

    private static uint PixelOf(MemoryBuffer buffer, int x, int y)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                return *(uint*)((byte*)view.Data + (y * view.Stride) + (x * 4)) | 0xFF000000u;
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    [Fact]
    public void A_known_serial_goes_out_before_created()
    {
        using var host = new CompositorTestHost();
        var publisher = new FakePublisher { NodeId = 77, ObjectSerial = 0x1_0000_0002UL };
        using var manager = new ScreencastManager(host.Display, publisher, null, null, null, null);

        var proxy = Bind(host);
        var events = StreamEvents.Attach(proxy.StreamOutput(host.Client.Outputs[0], 0));
        host.PumpUntil(() => events.Created == 1);

        Assert.Equal(["serial", "created"], events.Order);
        Assert.Equal(77u, events.Node);
        Assert.Equal(0x1_0000_0002UL, events.Serial);
    }

    [Fact]
    public void A_zero_serial_sends_created_alone()
    {
        using var host = new CompositorTestHost();
        var publisher = new FakePublisher { ObjectSerial = 0 };
        using var manager = new ScreencastManager(host.Display, publisher, null, null, null, null);

        var proxy = Bind(host);
        var events = StreamEvents.Attach(proxy.StreamOutput(host.Client.Outputs[0], 0));
        host.PumpUntil(() => events.Created == 1);

        Assert.Equal(["created"], events.Order);
    }

    [Fact]
    public void A_version_five_client_gets_created_alone()
    {
        using var host = new CompositorTestHost();
        var publisher = new FakePublisher { ObjectSerial = 42 };
        using var manager = new ScreencastManager(host.Display, publisher, null, null, null, null);

        var proxy = Bind(host, version: 5);
        var events = StreamEvents.Attach(proxy.StreamOutput(host.Client.Outputs[0], 0));
        host.PumpUntil(() => events.Created == 1);

        Assert.Equal(["created"], events.Order);
    }

    [Fact]
    public void A_known_window_uuid_resolves_to_its_toplevel()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        var id = model.Add("window", "app");
        var publisher = new FakePublisher();
        using var manager = new ScreencastManager(host.Display, publisher, null, model, null, null);

        var proxy = Bind(host);
        var events = StreamEvents.Attach(proxy.StreamWindow($"basin-{id}", 0));
        host.PumpUntil(() => events.Created == 1);

        var request = Assert.Single(publisher.Requests);
        Assert.Equal(CaptureSourceKind.Toplevel, request.Source.Kind);
        Assert.Equal(id, request.Source.ToplevelId);
    }

    [Fact]
    public void An_unknown_window_uuid_fails_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        var publisher = new FakePublisher();
        using var manager = new ScreencastManager(host.Display, publisher, null, new TestToplevelModel(), null, null);

        var proxy = Bind(host);
        var events = StreamEvents.Attach(proxy.StreamWindow("basin-999", 0));
        host.PumpUntil(() => events.Failed == 1);

        Assert.Equal("unknown window", events.Error);
        Assert.Empty(publisher.Requests);
        AssertClientAlive(host);
    }

    [Fact]
    public void A_region_inside_one_output_matches_the_output_capture()
    {
        using var host = new CompositorTestHost();
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var rect = new Scene.SceneRect(host.Scene.Root, 60, 20, new RenderColor(1f, 0f, 0f, 1f));
        rect.SetPosition(10, 10);

        var whole = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        Assert.True(capture.Capture(CaptureSource.Output(host.Output), default, whole));

        var source = CaptureSource.Region(new Box(20, 15, 40, 30), 1);
        Assert.True(capture.Supports(source));
        Assert.True(capture.TryDescribe(source, out var format));
        Assert.Equal((40, 30), (format.Width, format.Height));
        var region = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        Assert.True(capture.Capture(source, default, region));

        Assert.Equal(PixelOf(whole, 25, 20), PixelOf(region, 5, 5));
        Assert.Equal(PixelOf(whole, 55, 40), PixelOf(region, 35, 25));
        Assert.Equal(0xFFFF0000u, PixelOf(region, 5, 5));
        Assert.NotEqual(0xFFFF0000u, PixelOf(region, 35, 25));

        whole.Destroy();
        region.Destroy();
        rect.Destroy();
    }

    [Fact]
    public void A_region_spanning_two_outputs_captures_both()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 160, 0);
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var left = new Scene.SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        left.SetPosition(140, 40);
        var right = new Scene.SceneRect(host.Scene.Root, 20, 20, new RenderColor(0f, 1f, 0f, 1f));
        right.SetPosition(160, 40);

        var source = CaptureSource.Region(new Box(130, 30, 60, 40), 1);
        Assert.True(capture.TryDescribe(source, out var format));
        var target = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        Assert.True(capture.Capture(source, default, target));

        Assert.Equal(0xFFFF0000u, PixelOf(target, 15, 15));
        Assert.Equal(0xFF00FF00u, PixelOf(target, 45, 15));

        target.Destroy();
        left.Destroy();
        right.Destroy();
        second.Destroy();
    }

    [Fact]
    public void A_region_over_a_layout_gap_paints_the_background()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 200, 0);
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout)
        {
            Renderer = host.Renderer,
            Background = new RenderColor(0f, 0f, 1f, 1f),
        };

        var source = CaptureSource.Region(new Box(150, 10, 60, 20), 1);
        Assert.True(capture.TryDescribe(source, out var format));
        var target = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        Assert.True(capture.Capture(source, default, target));

        Assert.Equal(0xFF0000FFu, PixelOf(target, 25, 10));

        target.Destroy();
        second.Destroy();
    }

    [Fact]
    public void A_zero_scale_resolves_to_the_highest_intersected_scale()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        using (var state = new OutputState())
        {
            Assert.True(second.Commit(state.SetScale(2)));
        }

        host.Layout.Add(second, 160, 0);
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };

        var straddling = CaptureSource.Region(new Box(150, 0, 20, 10), 0);
        Assert.True(capture.TryDescribe(straddling, out var format));
        Assert.Equal((40, 20), (format.Width, format.Height));

        var firstOnly = CaptureSource.Region(new Box(10, 10, 20, 10), 0);
        Assert.True(capture.TryDescribe(firstOnly, out var alone));
        Assert.Equal((20, 10), (alone.Width, alone.Height));

        second.Destroy();
    }

    [Fact]
    public void A_client_named_scale_wins_over_the_outputs()
    {
        using var host = new CompositorTestHost();
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };

        var source = CaptureSource.Region(new Box(10, 10, 20, 10), 3);
        Assert.True(capture.TryDescribe(source, out var format));
        Assert.Equal((60, 30), (format.Width, format.Height));
    }

    [Fact]
    public void A_region_on_no_output_fails_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var publisher = new FakePublisher();
        using var manager = new ScreencastManager(
            host.Display, publisher, null, null, capture, new LayoutOutputSet(host.Layout));

        var proxy = Bind(host);
        var events = StreamEvents.Attach(
            proxy.StreamRegion(5000, 5000, 40, 40, WlFixed.FromDouble(1), 0));
        host.PumpUntil(() => events.Failed == 1);

        Assert.Equal("the region is not on any output", events.Error);
        Assert.Empty(publisher.Requests);
        AssertClientAlive(host);
    }

    [Fact]
    public void A_region_survives_one_output_and_closes_with_the_last()
    {
        using var host = new CompositorTestHost();
        var left = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        var right = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(left, 160, 0);
        host.Layout.Add(right, 320, 0);
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var publisher = new FakePublisher();
        using var manager = new ScreencastManager(
            host.Display, publisher, null, null, capture, new LayoutOutputSet(host.Layout));

        var proxy = Bind(host);
        var events = StreamEvents.Attach(
            proxy.StreamRegion(310, 10, 20, 20, WlFixed.FromDouble(1), 0));
        host.PumpUntil(() => events.Created == 1);

        left.Destroy();
        host.PumpToClient();
        Assert.Equal(0, events.Closed);
        Assert.Empty(publisher.ClosedStreams);

        right.Destroy();
        host.PumpUntil(() => events.Closed == 1);
        Assert.Single(publisher.ClosedStreams);
        Assert.Equal(0, events.Failed);
    }

    [Fact]
    public void A_rotated_output_contributes_upright_pixels()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        using (var state = new OutputState())
        {
            Assert.True(second.Commit(state.SetTransform(OutputTransform.Rotate90)));
        }

        host.Layout.Add(second, 160, 0);
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var rect = new Scene.SceneRect(host.Scene.Root, 20, 10, new RenderColor(1f, 0f, 0f, 1f));
        rect.SetPosition(170, 20);

        var source = CaptureSource.Region(new Box(160, 10, 60, 40), 1);
        Assert.True(capture.TryDescribe(source, out var format));
        var target = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        Assert.True(capture.Capture(source, default, target));

        Assert.Equal(0xFFFF0000u, PixelOf(target, 15, 15));
        Assert.NotEqual(0xFFFF0000u, PixelOf(target, 45, 35));

        target.Destroy();
        rect.Destroy();
        second.Destroy();
    }

    [Fact]
    public void A_virtual_output_is_created_and_destroyed_with_its_stream()
    {
        using var host = new CompositorTestHost();
        var publisher = new FakePublisher();
        var factory = new FakeVirtualOutputs(host);
        using var manager = new ScreencastManager(host.Display, publisher, factory, null, null, null);

        var proxy = Bind(host);
        var stream = proxy.StreamVirtualOutput("cast", 320, 200, WlFixed.FromDouble(1.5), 0);
        var events = StreamEvents.Attach(stream);
        host.PumpUntil(() => events.Created == 1);

        var created = Assert.Single(factory.Created);
        Assert.Equal(("cast", 320, 200, 1.5), (created.Name, created.Width, created.Height, created.Scale));
        var request = Assert.Single(publisher.Requests);
        Assert.Equal(CaptureSourceKind.Output, request.Source.Kind);
        Assert.Same(factory.LastOutput, request.Source.OutputTarget);
        Assert.Empty(factory.Destroyed);

        stream.Close();
        host.PumpUntil(() => factory.Destroyed.Count == 1);
        Assert.Same(factory.LastOutput, factory.Destroyed[0]);
        Assert.Single(publisher.ClosedStreams);
    }

    [Fact]
    public void A_description_travels_with_the_virtual_output()
    {
        using var host = new CompositorTestHost();
        var publisher = new FakePublisher();
        var factory = new FakeVirtualOutputs(host);
        using var manager = new ScreencastManager(host.Display, publisher, factory, null, null, null);

        var proxy = Bind(host);
        var stream = proxy.StreamVirtualOutputWithDescription(
            "cast", "a described output", 100, 100, WlFixed.FromDouble(1), 0);
        var events = StreamEvents.Attach(stream);
        host.PumpUntil(() => events.Created == 1);

        Assert.Equal("a described output", Assert.Single(factory.Created).Description);
        stream.Close();
        host.PumpUntil(() => factory.Destroyed.Count == 1);
    }

    [Fact]
    public void The_cursor_bitmask_reaches_the_request_unchanged()
    {
        using var host = new CompositorTestHost();
        var publisher = new FakePublisher();
        using var manager = new ScreencastManager(host.Display, publisher, null, null, null, null);

        var proxy = Bind(host);
        var events = StreamEvents.Attach(proxy.StreamOutput(host.Client.Outputs[0], 2 | 4));
        host.PumpUntil(() => events.Created == 1);

        var request = Assert.Single(publisher.Requests);
        Assert.Equal(ScreencastCursorMode.Embedded | ScreencastCursorMode.Metadata, request.Cursor);
        Assert.True(request.Source.OverlayCursor);
    }

    [Fact]
    public void An_embedded_cursor_lands_at_the_region_local_position()
    {
        using var host = new CompositorTestHost();
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var image = new MemoryBuffer(8, 8, DrmFormat.Argb8888);
        Assert.True(image.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < 8; y++)
            {
                var row = (uint*)((byte*)view.Data + (y * view.Stride));
                for (var x = 0; x < 8; x++)
                {
                    row[x] = 0xFF3366CCu;
                }
            }
        }

        image.EndDataAccess();
        capture.SetCursor(image, new CaptureCursorState(50, 40, 0, 0, 8, 8, IsVisible: true));

        var source = CaptureSource.Region(new Box(30, 20, 60, 50), 1, overlayCursor: true);
        Assert.True(capture.TryDescribe(source, out var format));
        var target = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        Assert.True(capture.Capture(source, default, target));

        Assert.Equal(0xFF3366CCu, PixelOf(target, 22, 22));
        Assert.NotEqual(0xFF3366CCu, PixelOf(target, 10, 10));

        target.Destroy();
        image.Destroy();
    }

    [Fact]
    public void An_unplugged_output_closes_the_stream_rather_than_failing_it()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(64, 48, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 160, 0);
        var global = new OutputGlobal(host.Display, second);
        var publisher = new FakePublisher();
        using var manager = new ScreencastManager(host.Display, publisher, null, null, null, null);

        var proxy = Bind(host);
        var outputs = new List<WlOutput>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wl_output")
            {
                outputs.Add(registry.Bind<WlOutput>(e.Name, 4));
            }
        };
        host.PumpUntil(() => outputs.Count == 2);
        var events = StreamEvents.Attach(proxy.StreamOutput(outputs[1], 0));
        host.PumpUntil(() => events.Created == 1);

        host.Layout.Remove(second);
        second.Destroy();
        host.PumpUntil(() => events.Closed == 1);

        Assert.Equal(0, events.Failed);
        Assert.Single(publisher.ClosedStreams);
        global.Dispose();
    }

    [Fact]
    public void Failed_and_closed_are_never_both_sent()
    {
        using var host = new CompositorTestHost();
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var outputs = new LayoutOutputSet(host.Layout);
        using var manager = new ScreencastManager(host.Display, null, null, null, capture, outputs);

        var proxy = Bind(host);
        var events = StreamEvents.Attach(
            proxy.StreamRegion(10, 10, 20, 20, WlFixed.FromDouble(1), 0));
        host.PumpUntil(() => events.Failed == 1);

        host.Layout.Remove(host.Output);
        host.PumpToClient();
        Assert.Equal(0, events.Closed);
        Assert.Equal(1, events.Failed);

        host.Layout.Add(host.Output, 0, 0);
        AssertClientAlive(host);
    }
}
