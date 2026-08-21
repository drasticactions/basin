using System.Runtime.InteropServices;
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
