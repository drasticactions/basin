using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PointerGestureTests
{
    [Fact]
    public void Gestures_reach_the_focused_client()
    {
        using var host = new CompositorTestHost();
        using var manager = new PointerGesturesManager(host.Display, host.Seat);

        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 5, 5);

        Basin.Desktop.Protocol.ZwpPointerGesturesV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_pointer_gestures_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwpPointerGesturesV1>(e.Name, 3);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var clientPointer = host.Client.Seat!.GetPointer();
        var swipe = proxy!.GetSwipeGesture(clientPointer);
        var pinch = proxy.GetPinchGesture(clientPointer);
        var began = 0u;
        var updates = new List<(double Dx, double Dy)>();
        var ended = false;
        var pinchScales = new List<double>();
        swipe.Begin += (_, e) => began = e.Fingers;
        swipe.Update += (_, e) => updates.Add((e.Dx.ToDouble(), e.Dy.ToDouble()));
        swipe.End += (_, e) => ended = e.Cancelled != 0;
        pinch.Update += (_, e) => pinchScales.Add(e.Scale.ToDouble());
        host.PumpToServer();

        manager.NotifySwipeBegin(10, 3);
        manager.NotifySwipeUpdate(11, 4.5, -2.25);
        manager.NotifySwipeEnd(12, canceled: true);
        manager.NotifyPinchBegin(20, 2);
        manager.NotifyPinchUpdate(21, 0, 0, 1.5, 45.0);
        manager.NotifyPinchEnd(22);
        host.PumpUntil(() => pinchScales.Count == 1);

        Assert.Equal(3u, began);
        Assert.Equal((4.5, -2.25), Assert.Single(updates));
        Assert.True(ended);
        Assert.Equal(1.5, Assert.Single(pinchScales), 3);

        swipe.Dispose();
        pinch.Dispose();
        host.PumpToServer();
    }
}

public sealed class ShortcutsInhibitTests
{
    [Fact]
    public void Inhibitor_lifecycle_and_activation()
    {
        using var host = new CompositorTestHost();
        using var manager = new KeyboardShortcutsInhibitManager(host.Display, host.Compositor);
        Surface? inhibitedSurface = null;
        manager.InhibitorCreated += surface => inhibitedSurface = surface;

        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.ZwpKeyboardShortcutsInhibitManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_keyboard_shortcuts_inhibit_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwpKeyboardShortcutsInhibitManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var inhibitor = proxy!.InhibitShortcuts(window.Surface, host.Client.Seat!);
        var active = 0;
        var inactive = 0;
        inhibitor.Active += (_, _) => active++;
        inhibitor.Inactive += (_, _) => inactive++;
        host.PumpUntil(() => inhibitedSurface is not null);
        Assert.Same(window.ServerSurface, inhibitedSurface);
        Assert.True(manager.IsInhibited(window.ServerSurface));

        manager.Activate(window.ServerSurface);
        host.PumpUntil(() => active == 1);
        manager.Deactivate(window.ServerSurface);
        host.PumpUntil(() => inactive == 1);

        inhibitor.Dispose();
        host.PumpToServer();
        Assert.False(manager.IsInhibited(window.ServerSurface));
    }
}

public sealed class VirtualInputTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void Virtual_keyboard_injections_surface_as_events()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingInputSink();
        using var manager = new VirtualKeyboardManager(host.Display, sink);
        var keymaps = new List<uint>();
        manager.KeymapSubmitted += (fd, size) =>
        {
            keymaps.Add(size);
            fd.Close();
        };

        Basin.Desktop.Protocol.ZwpVirtualKeyboardManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_virtual_keyboard_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwpVirtualKeyboardManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var keyboard = proxy!.CreateVirtualKeyboard(host.Client.Seat!);
        var fd = memfd_create("keymap", 0);
        keyboard.Keymap(Wayland.WlKeyboard.KeymapFormat.XkbV1, fd, 42);
        close(fd);
        keyboard.Key(5, 30, 1);
        keyboard.Key(6, 30, 0);
        keyboard.Modifiers(4, 0, 0, 2);
        host.PumpUntil(() => sink.Keys.Count == 2 && keymaps.Count == 1);

        Assert.Equal(42u, keymaps[0]);
        Assert.Equal((5u, 30u, true), sink.Keys[0]);
        Assert.Equal((6u, 30u, false), sink.Keys[1]);
        Assert.Equal((4u, 0u, 0u, 2u), sink.ModifierEvents[^1]);

        keyboard.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Virtual_keyboard_keymaps_follow_the_active_device()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap(new Basin.Capabilities.KeymapNames(Layout: "fr"));
        var sink = new Basin.Seat.SeatInputSink(host.Seat);
        using var manager = new VirtualKeyboardManager(host.Display, sink);

        var seatKeyboard = host.Client.Seat!.GetKeyboard();
        var keymapEvents = 0;
        seatKeyboard.Keymap += (_, e) =>
        {
            keymapEvents++;
            close(e.Fd);
        };
        host.PumpUntil(() => keymapEvents == 1);

        Basin.Desktop.Protocol.ZwpVirtualKeyboardManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_virtual_keyboard_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwpVirtualKeyboardManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var keyboard = proxy!.CreateVirtualKeyboard(host.Client.Seat!);
        using var context = Xkb.XkbContext.Create();
        using var us = context.CreateKeymap(new Xkb.XkbRuleNames { Layout = "us" });
        using var keymap = new Basin.Capabilities.Keymap(us.AsString());
        keyboard.Keymap(Wayland.WlKeyboard.KeymapFormat.XkbV1, keymap.Fd, keymap.Size);
        keyboard.Key(5, 16, 1);
        host.PumpUntil(() => keymapEvents == 2);

        Assert.Equal(Xkb.XkbKeysym.FromName("q"), host.Seat.Keyboard.KeysymFor(16));

        keyboard.Key(6, 16, 0);
        host.PumpToServer();
        sink.Key(null, 7, 16, true);
        sink.Key(null, 8, 16, false);
        host.PumpUntil(() => keymapEvents == 3);

        Assert.Equal(Xkb.XkbKeysym.FromName("a"), host.Seat.Keyboard.KeysymFor(16));

        keyboard.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Virtual_pointer_injections_surface_as_events()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingInputSink();
        using var manager = new VirtualPointerManager(host.Display, sink);

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

        var pointer = proxy!.CreateVirtualPointer(host.Client.Seat);
        pointer.Motion(1, WlFixed.FromDouble(3.5), WlFixed.FromDouble(-1.5));
        pointer.MotionAbsolute(2, 512, 384, 1024, 768);
        pointer.Button(3, 0x110, WlPointer.ButtonState.Pressed);
        pointer.Frame();
        pointer.AxisSource(WlPointer.AxisSource.Finger);
        pointer.Axis(4, WlPointer.Axis.VerticalScroll, WlFixed.FromDouble(9.5));
        pointer.Frame();
        pointer.AxisStop(5, WlPointer.Axis.VerticalScroll);
        pointer.Frame();
        host.PumpUntil(() => sink.AxisStops.Count == 1);

        Assert.Equal((1u, 3.5, -1.5), Assert.Single(sink.Motions));

        Assert.Equal((2u, 512.0, 384.0, 1024.0, 768.0), Assert.Single(sink.AbsoluteMotions));
        Assert.Equal((3u, 0x110u, true), Assert.Single(sink.Buttons));
        Assert.Equal((uint)WlPointer.AxisSource.Finger, Assert.Single(sink.AxisSources));
        Assert.Equal((4u, (uint)WlPointer.Axis.VerticalScroll, 9.5), Assert.Single(sink.Axes));
        Assert.Equal((5u, (uint)WlPointer.Axis.VerticalScroll), Assert.Single(sink.AxisStops));
        Assert.Equal(3, sink.Frames);

        pointer.Dispose();
        host.PumpToServer();
    }
}

public sealed class TransientSeatTests
{
    [Fact]
    public void Requests_deny_by_default_and_honor_ready()
    {
        using var host = new CompositorTestHost();
        using var manager = new TransientSeatManager(host.Display);

        Basin.Desktop.Protocol.ExtTransientSeatManagerV1? proxy = null;
        var seatNames = new List<uint>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_transient_seat_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ExtTransientSeatManagerV1>(e.Name, 1);
            }
            else if (e.Interface == "wl_seat")
            {
                seatNames.Add(e.Name);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var first = proxy!.Create();
        var denied = false;
        first.Denied += (_, _) => denied = true;
        host.PumpUntil(() => denied);

        var alreadyAdvertised = seatNames.ToArray();
        Basin.Seat.Seat? transient = null;
        manager.SeatRequested += request =>
        {
            transient = new Basin.Seat.Seat(host.Display, host.Compositor, "transient");
            request.Ready(transient);
        };

        var second = proxy.Create();
        var readyName = 0u;
        second.Ready += (_, e) => readyName = e.GlobalName;
        host.PumpUntil(() => readyName != 0);

        var advertised = Assert.Single(seatNames.Except(alreadyAdvertised));
        Assert.Equal(advertised, readyName);

        var bound = registry.Bind<Wayland.WlSeat>(readyName, 2);
        var boundName = string.Empty;
        bound.Name += (_, e) => boundName = e.Name;
        host.PumpUntil(() => boundName.Length > 0);
        Assert.Equal("transient", boundName);

        bound.Dispose();
        first.Dispose();
        second.Dispose();
        host.PumpToServer();
        transient?.Dispose();
    }

    [Fact]
    public void A_created_seat_takes_the_virtual_devices_aimed_at_it()
    {
        using var host = new CompositorTestHost();
        var fallback = new RecordingInputSink();
        var routed = new RecordingInputSink();
        using var manager = new TransientSeatManager(host.Display, host.Compositor);
        using var pointers = new VirtualPointerManager(host.Display, fallback);

        Basin.Seat.Seat? created = null;
        manager.SeatCreated += seat => created = seat;
        manager.SeatRequested += request => request.Create(_ => routed);

        Basin.Desktop.Protocol.ExtTransientSeatManagerV1? seats = null;
        Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1? virtualPointers = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_transient_seat_manager_v1")
            {
                seats = registry.Bind<Basin.Desktop.Protocol.ExtTransientSeatManagerV1>(e.Name, 1);
            }
            else if (e.Interface == "zwlr_virtual_pointer_manager_v1")
            {
                virtualPointers = registry.Bind<Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1>(e.Name, 2);
            }
        };
        host.PumpToClient();
        Assert.NotNull(seats);
        Assert.NotNull(virtualPointers);

        var handle = seats!.Create();
        var readyName = 0u;
        handle.Ready += (_, e) => readyName = e.GlobalName;
        host.PumpUntil(() => readyName != 0);
        Assert.NotNull(created);
        Assert.Equal("seat-1", created!.Name);

        var bound = registry.Bind<Wayland.WlSeat>(readyName, 7);
        var capabilities = new List<WlSeat.Capability>();
        bound.Capabilities += (_, e) => capabilities.Add(e.Capabilities);
        host.PumpUntil(() => capabilities.Count == 1);
        Assert.Equal(default, capabilities[0]);

        var pointer = virtualPointers!.CreateVirtualPointer(bound);
        pointer.Motion(1, WlFixed.FromDouble(4), WlFixed.FromDouble(5));
        pointer.Frame();
        host.PumpUntil(() => routed.Frames == 1 && capabilities.Count == 2);
        Assert.Equal((1u, 4.0, 5.0), Assert.Single(routed.Motions));
        Assert.Empty(fallback.Motions);
        Assert.Equal(WlSeat.Capability.Pointer, capabilities[1]);

        pointer.Dispose();
        host.PumpUntil(() => capabilities.Count == 3);
        Assert.Equal(default, capabilities[2]);

        bound.Dispose();
        handle.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_seat_with_no_factory_maps_absolute_motion_onto_the_layout()
    {
        using var host = new CompositorTestHost();
        using var manager = new TransientSeatManager(host.Display, host.Compositor, layout: host.Layout);
        using var pointers = new VirtualPointerManager(host.Display, sink: null);
        Basin.Seat.Seat? created = null;
        manager.SeatRequested += request => created = request.Create();

        Basin.Desktop.Protocol.ExtTransientSeatManagerV1? seats = null;
        Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1? virtualPointers = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_transient_seat_manager_v1")
            {
                seats = registry.Bind<Basin.Desktop.Protocol.ExtTransientSeatManagerV1>(e.Name, 1);
            }
            else if (e.Interface == "zwlr_virtual_pointer_manager_v1")
            {
                virtualPointers = registry.Bind<Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1>(e.Name, 2);
            }
        };
        host.PumpToClient();
        var window = MappedToplevel.Map(host, host.Client);

        var handle = seats!.Create();
        var readyName = 0u;
        handle.Ready += (_, e) => readyName = e.GlobalName;
        host.PumpUntil(() => readyName != 0);

        var bound = registry.Bind<Wayland.WlSeat>(readyName, 7);
        var pointerProxy = bound.GetPointer();
        var motions = new List<(double X, double Y)>();
        pointerProxy.Motion += (_, e) => motions.Add((e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble()));
        var pointer = virtualPointers!.CreateVirtualPointer(bound);
        host.PumpToServer();
        created!.Pointer.NotifyMotionAt(1, window.ServerSurface, 0, 0, 0, 0);
        host.PumpUntil(() => motions.Count == 1);

        pointer.MotionAbsolute(2, 1, 1, 2, 2);
        pointer.Frame();
        host.PumpUntil(() => motions.Count == 2);
        var mode = host.Output.CurrentMode;
        Assert.Equal((mode.Width / 2.0, mode.Height / 2.0), motions[1]);

        pointer.Dispose();
        pointerProxy.Dispose();
        bound.Dispose();
        handle.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_scene_driven_seat_enters_the_surface_under_its_pointer()
    {
        using var host = new CompositorTestHost();
        using var manager = new TransientSeatManager(host.Display, host.Compositor);
        using var pointers = new VirtualPointerManager(host.Display, sink: null);

        manager.SeatRequested += request =>
            request.Create(seat => new Basin.Desktop.SceneSeatInput(seat, host.Scene, host.Layout));

        Basin.Desktop.Protocol.ExtTransientSeatManagerV1? seats = null;
        Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1? virtualPointers = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_transient_seat_manager_v1")
            {
                seats = registry.Bind<Basin.Desktop.Protocol.ExtTransientSeatManagerV1>(e.Name, 1);
            }
            else if (e.Interface == "zwlr_virtual_pointer_manager_v1")
            {
                virtualPointers = registry.Bind<Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1>(e.Name, 2);
            }
        };
        host.PumpToClient();

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Solid(64, 48, 0xFF204060));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpUntil(() => host.SurfaceScenes.Count == 1);

        var handle = seats!.Create();
        var readyName = 0u;
        handle.Ready += (_, e) => readyName = e.GlobalName;
        host.PumpUntil(() => readyName != 0);

        var bound = registry.Bind<Wayland.WlSeat>(readyName, 7);
        var pointerProxy = bound.GetPointer();
        var entered = 0;
        var at = (X: 0.0, Y: 0.0);
        pointerProxy.Enter += (_, e) =>
        {
            entered++;
            at = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
        };

        var mode = host.Output.CurrentMode;
        var pointer = virtualPointers!.CreateVirtualPointer(bound);
        pointer.MotionAbsolute(1, 20, 10, (uint)mode.Width, (uint)mode.Height);
        pointer.Frame();
        host.PumpUntil(() => entered == 1);
        Assert.Equal((20.0, 10.0), at);

        pointer.Dispose();
        pointerProxy.Dispose();
        bound.Dispose();
        handle.Dispose();
        host.PumpToServer();
    }
}

public sealed class CursorShapeTests
{
    [Fact]
    public void A_version_two_shape_resolves_for_a_client_bound_at_two()
    {
        using var host = new CompositorTestHost();
        using var manager = new CursorShapeManager(host.Display, theme: null);
        var shapes = new List<Basin.Capabilities.CursorShape>();
        manager.ShapeRequested += shapes.Add;

        var device = Device(host, CursorShapeManager.Version);
        device.SetShape(0, (Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape)35);
        host.PumpUntil(() => shapes.Count == 1);

        Assert.Equal(Basin.Capabilities.CursorShape.DndAsk, Assert.Single(shapes));
        Assert.Equal("dnd-ask", Basin.Capabilities.CursorShapeNames.NameOf(Basin.Capabilities.CursorShape.DndAsk));
    }

    [Fact]
    public void A_version_two_shape_is_an_error_for_a_client_bound_at_one()
    {
        using var host = new CompositorTestHost();
        using var manager = new CursorShapeManager(host.Display, theme: null);
        var shapes = 0;
        manager.ShapeRequested += _ => shapes++;

        var survivor = host.ConnectClient();
        var device = Device(host, 1);
        device.SetShape(0, (Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape)35);

        AssertKilled(host);
        Assert.Equal(0, shapes);
        AssertAlive(host, survivor);
    }

    [Theory]
    [InlineData(1u, 0u)]
    [InlineData(1u, 37u)]
    [InlineData((uint)CursorShapeManager.Version, 0u)]
    [InlineData((uint)CursorShapeManager.Version, 37u)]
    public void Out_of_range_shapes_are_errors_at_every_version(uint bindVersion, uint shape)
    {
        using var host = new CompositorTestHost();
        using var manager = new CursorShapeManager(host.Display, theme: null);

        var device = Device(host, bindVersion);
        device.SetShape(0, (Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape)shape);

        AssertKilled(host);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(37u)]
    public void Out_of_range_shapes_on_a_tablet_tool_device_are_errors(uint shape)
    {
        using var rig = new ToolRig();
        rig.Device.SetShape(rig.Serials[^1], (Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape)shape);

        AssertKilled(rig.Host);
        Assert.Empty(rig.Shapes);
    }

    [Fact]
    public void A_tool_shape_with_the_proximity_serial_names_the_tool()
    {
        using var rig = new ToolRig();
        rig.Device.SetShape(rig.Serials[^1], Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape.Text);
        rig.Host.PumpUntil(() => rig.Shapes.Count == 1);

        var (tool, shape) = Assert.Single(rig.Shapes);
        Assert.Same(rig.Tool, tool);
        Assert.Equal(Basin.Capabilities.CursorShape.Text, shape);
    }

    [Fact]
    public void A_tool_shape_with_an_old_serial_raises_nothing()
    {
        using var rig = new ToolRig();
        var old = rig.Serials[^1];
        rig.Cycle();

        rig.Device.SetShape(old, Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape.Text);
        rig.Device.SetShape(rig.Serials[^1], Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape.Crosshair);
        rig.Host.PumpUntil(() => rig.Shapes.Count == 1);
        rig.Host.PumpToServer();

        Assert.Equal(Basin.Capabilities.CursorShape.Crosshair, Assert.Single(rig.Shapes).Shape);
    }

    [Fact]
    public void A_tool_shape_without_a_tablet_manager_is_accepted_and_raises_nothing()
    {
        using var rig = new ToolRig(withTablets: false);
        rig.Device.SetShape(rig.Serials[^1], Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape.Text);
        rig.Host.PumpToServer();
        rig.Host.PumpToClient();

        Assert.Empty(rig.Shapes);
        AssertAlive(rig.Host, rig.Host.Client);
    }

    private sealed class ToolRig : IDisposable
    {
        private readonly Basin.Backend.Headless.HeadlessTablet _source;
        private readonly TabletManager _tablets;
        private readonly CursorShapeManager _shapes;
        private readonly Basin.Desktop.Protocol.ZwpTabletSeatV2 _tabletSeat;
        private readonly ulong _toolId;
        private readonly ulong _tabletId;

        public ToolRig(bool withTablets = true)
        {
            Host = new CompositorTestHost();
            _source = Host.Backend.CreateTablet();
            _tablets = new TabletManager(Host.Display, _source);
            _shapes = new CursorShapeManager(Host.Display, theme: null, tablets: withTablets ? _tablets : null);
            _shapes.ToolShapeRequested += (tool, shape) => Shapes.Add((tool, shape));
            var window = MappedToplevel.Map(Host, Host.Client);

            Basin.Desktop.Protocol.ZwpTabletManagerV2? tabletProxy = null;
            Basin.Desktop.Protocol.WpCursorShapeManagerV1? shapeProxy = null;
            var registry = Host.Client.Display.GetRegistry();
            registry.Global += (_, e) =>
            {
                if (e.Interface == "zwp_tablet_manager_v2")
                {
                    tabletProxy = registry.Bind<Basin.Desktop.Protocol.ZwpTabletManagerV2>(e.Name, 1);
                }
                else if (e.Interface == "wp_cursor_shape_manager_v1")
                {
                    shapeProxy = registry.Bind<Basin.Desktop.Protocol.WpCursorShapeManagerV1>(e.Name, CursorShapeManager.Version);
                }
            };
            Host.PumpToClient();

            _tabletSeat = tabletProxy!.GetTabletSeat(Host.Client.Seat!);
            Basin.Desktop.Protocol.ZwpTabletToolV2? toolProxy = null;
            _tabletSeat.ToolAdded += (_, e) => toolProxy = e.Id;
            Host.PumpToServer();

            _tabletId = _source.AddTablet("Shape Tablet").Id;
            _toolId = _source.AddTool(TabletToolType.Pen, 0x42, TabletToolAxis.Pressure).Id;
            Host.PumpUntil(() => toolProxy is not null);
            toolProxy!.ProximityIn += (_, e) => Serials.Add(e.Serial);
            Device = shapeProxy!.GetTabletToolV2(toolProxy);

            _tablets.ToolProximityIn += (tool, _, axes) =>
            {
                Tool = tool;
                tool.SetFocus(window.ServerSurface, axes.X * 60, axes.Y * 50);
            };
            Cycle();
        }

        public CompositorTestHost Host { get; }

        public Basin.Desktop.Protocol.WpCursorShapeDeviceV1 Device { get; }

        public List<uint> Serials { get; } = [];

        public List<(TabletManager.TabletTool Tool, Basin.Capabilities.CursorShape Shape)> Shapes { get; } = [];

        public TabletManager.TabletTool? Tool { get; private set; }

        public void Cycle()
        {
            if (Serials.Count > 0)
            {
                _source.InjectProximity(_toolId, _tabletId, inProximity: false);
            }

            var count = Serials.Count;
            _source.InjectAxis(_toolId, 2, new TabletToolAxes(0.5, 0.5, 0, 0, 0, 0, 0, 0, 0));
            _source.InjectProximity(_toolId, _tabletId, inProximity: true);
            Host.PumpUntil(() => Serials.Count > count);
        }

        public void Dispose()
        {
            _shapes.Dispose();
            _tablets.Dispose();
            Host.Dispose();
        }
    }

    private static Basin.Desktop.Protocol.WpCursorShapeDeviceV1 Device(CompositorTestHost host, uint version)
    {
        Basin.Desktop.Protocol.WpCursorShapeManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_cursor_shape_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpCursorShapeManagerV1>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!.GetPointer(host.Client.Seat!.GetPointer());
    }

    private static void AssertKilled(CompositorTestHost host)
    {
        host.PumpToServer();
        host.Display.FlushClients();
        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    private static void AssertAlive(CompositorTestHost host, ShmTestClient client)
    {
        var sync = client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        for (var i = 0; i < 20 && !done; i++)
        {
            client.Display.Flush();
            host.Loop.Dispatch(0);
            host.Display.FlushClients();
            client.Display.Dispatch();
        }

        Assert.True(done);
    }
}
