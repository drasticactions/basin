using Basin.Capabilities;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

internal static class LibcCloseHelper
{
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close")]
    public static extern int Close(int fd);
}

public sealed class TextInputRelayTests
{
    [Fact]
    public void Focus_handoff_from_a_dying_surface_survives()
    {
        using var host = new CompositorTestHost();
        using var manager = new TextInputManager(host.Display, host.Seat, new InputMethodRelay(host.Display, host.Seat));
        var doomed = MappedToplevel.Map(host, host.Client);
        var survivor = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.ZwpTextInputManagerV3? tiManager = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_text_input_manager_v3")
            {
                tiManager = registry.Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var textInput = tiManager!.GetTextInput(host.Client.Seat!);
        var entered = 0;
        textInput.Enter += (_, _) => entered++;
        host.PumpToServer();

        manager.NotifyFocus(doomed.ServerSurface);
        host.PumpUntil(() => entered == 1);

        doomed.ServerSurface.Destroyed += () => manager.NotifyFocus(survivor.ServerSurface);
        doomed.Toplevel.Dispose();
        doomed.XdgSurface.Dispose();
        doomed.Surface.Dispose();
        host.PumpUntil(() => entered == 2);
    }

    [Fact]
    public void Text_input_and_input_method_relay_both_ways()
    {
        using var host = new CompositorTestHost();
        using var manager = new TextInputManager(host.Display, host.Seat, new InputMethodRelay(host.Display, host.Seat));
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.ZwpTextInputManagerV3? tiManager = null;
        Basin.Desktop.Protocol.ZwpInputMethodManagerV2? imManager = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "zwp_text_input_manager_v3":
                    tiManager = registry.Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(e.Name, 1);
                    break;
                case "zwp_input_method_manager_v2":
                    imManager = registry.Bind<Basin.Desktop.Protocol.ZwpInputMethodManagerV2>(e.Name, 1);
                    break;
            }
        };
        host.PumpToClient();
        Assert.NotNull(tiManager);
        Assert.NotNull(imManager);

        var textInput = tiManager!.GetTextInput(host.Client.Seat!);
        var entered = 0;
        var left = 0;
        var preedits = new List<string?>();
        var commits = new List<string?>();
        var doneSerials = new List<uint>();
        textInput.Enter += (_, _) => entered++;
        textInput.Leave += (_, _) => left++;
        textInput.PreeditString += (_, e) => preedits.Add(e.Text);
        textInput.CommitString += (_, e) => commits.Add(e.Text);
        textInput.Done += (_, e) => doneSerials.Add(e.Serial);

        var inputMethod = imManager!.GetInputMethod(host.Client.Seat!);
        var activated = 0;
        var deactivated = 0;
        var imDone = 0;
        var surroundings = new List<string>();
        inputMethod.Activate += (_, _) => activated++;
        inputMethod.Deactivate += (_, _) => deactivated++;
        inputMethod.Done += (_, _) => imDone++;
        inputMethod.SurroundingText += (_, e) => surroundings.Add(e.Text);
        host.PumpToServer();

        manager.NotifyFocus(window.ServerSurface);
        host.PumpUntil(() => entered == 1);

        textInput.Enable();
        textInput.SetSurroundingText("hello", 5, 5);
        textInput.Commit();
        host.PumpUntil(() => activated == 1 && imDone >= 1);
        Assert.Equal("hello", Assert.Single(surroundings));

        inputMethod.SetPreeditString("にほ", 0, 6);
        inputMethod.Commit(1);
        host.PumpUntil(() => doneSerials.Count == 1);
        Assert.Equal("にほ", Assert.Single(preedits));

        inputMethod.CommitString("日本語");
        inputMethod.Commit(2);
        host.PumpUntil(() => doneSerials.Count == 2);
        Assert.Equal("日本語", Assert.Single(commits));

        manager.NotifyFocus(null);
        host.PumpUntil(() => left == 1 && deactivated >= 1);

        host.Seat.Keyboard.SetKeymap();
        var grab = inputMethod.GrabKeyboard();
        var grabKeymaps = new List<uint>();
        var grabKeys = new List<(uint Key, uint State)>();
        grab.Keymap += (_, e) =>
        {
            grabKeymaps.Add(e.Size);
            LibcCloseHelper.Close(e.Fd);
        };
        grab.Key += (_, e) => grabKeys.Add((e.Key, (uint)e.State));
        host.PumpToServer();
        Assert.True(manager.HasKeyboardGrab);

        manager.ForwardKey(50, 30, pressed: true);
        manager.ForwardKey(51, 30, pressed: false);
        host.PumpUntil(() => grabKeys.Count == 2);
        Assert.True(grabKeymaps.Count == 1 && grabKeymaps[0] > 0);
        Assert.Equal((30u, 1u), grabKeys[0]);
        Assert.Equal((30u, 0u), grabKeys[1]);

        grab.Release();
        host.PumpToServer();
        Assert.False(manager.HasKeyboardGrab);

        var second = imManager.GetInputMethod(host.Client.Seat!);
        var unavailable = false;
        second.Unavailable += (_, _) => unavailable = true;
        host.PumpUntil(() => unavailable);

        textInput.Dispose();
        inputMethod.Dispose();
        second.Dispose();
        host.PumpToServer();
    }
}

public sealed class TabletTests
{
    [Fact]
    public void Devices_announce_and_tool_events_route_to_the_surface()
    {
        using var host = new CompositorTestHost();
        using var manager = new TabletManager(host.Display);
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.ZwpTabletManagerV2? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_tablet_manager_v2")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwpTabletManagerV2>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var tabletSeat = proxy!.GetTabletSeat(host.Client.Seat!);
        Basin.Desktop.Protocol.ZwpTabletV2? tabletProxy = null;
        Basin.Desktop.Protocol.ZwpTabletToolV2? toolProxy = null;
        Basin.Desktop.Protocol.ZwpTabletPadV2? padProxy = null;
        string? tabletName = null;
        var toolType = 0u;
        var capabilities = new List<uint>();
        uint padButtons = 0;
        tabletSeat.TabletAdded += (_, e) =>
        {
            tabletProxy = e.Id;
            tabletProxy.Name += (_, ne) => tabletName = ne.Name;
        };
        tabletSeat.ToolAdded += (_, e) =>
        {
            toolProxy = e.Id;
            toolProxy.TypeEvent += (_, te) => toolType = (uint)te.ToolType;
            toolProxy.CapabilityEvent += (_, ce) => capabilities.Add((uint)ce.Capability);
        };
        tabletSeat.PadAdded += (_, e) =>
        {
            padProxy = e.Id;
            padProxy.Buttons += (_, be) => padButtons = be.Buttons;
        };
        host.PumpToServer();

        var tablet = manager.AddTablet("Wacom Test", 0x056a, 0x0357, "/dev/input/event42");
        var tool = manager.AddTool(
            Basin.Desktop.Protocol.ZwpTabletToolV2.Type.Pen,
            0xDEADBEEFCAFE,
            TabletManager.TabletToolCapabilities.Pressure | TabletManager.TabletToolCapabilities.Tilt);
        var pad = manager.AddPad("/dev/input/event43", 8);
        host.PumpUntil(() => tabletProxy is not null && toolProxy is not null && padProxy is not null);
        Assert.Equal("Wacom Test", tabletName);
        Assert.Equal(320u, toolType);
        Assert.Equal(2, capabilities.Count);
        Assert.Equal(8u, padButtons);

        var motions = new List<(double X, double Y)>();
        var pressures = new List<uint>();
        var frames = new List<uint>();
        var downs = 0;
        var proximityIns = 0;
        var proximityOuts = 0;
        toolProxy!.ProximityIn += (_, _) => proximityIns++;
        toolProxy.ProximityOut += (_, _) => proximityOuts++;
        toolProxy.Motion += (_, e) => motions.Add((e.X.ToDouble(), e.Y.ToDouble()));
        toolProxy.Pressure += (_, e) => pressures.Add(e.Pressure);
        toolProxy.Down += (_, _) => downs++;
        toolProxy.Frame += (_, e) => frames.Add(e.Time);
        host.PumpToServer();

        tool.NotifyProximityIn(tablet, window.ServerSurface, 12.5, 30.25);
        tool.NotifyFrame(100);
        tool.NotifyDown();
        tool.NotifyPressure(0.5);
        tool.NotifyMotion(15, 32);
        tool.NotifyFrame(101);
        tool.NotifyProximityOut();
        tool.NotifyFrame(102);
        host.PumpUntil(() => proximityOuts == 1);

        Assert.Equal(1, proximityIns);
        Assert.Equal(1, downs);
        Assert.Equal(new[] { (12.5, 30.25), (15.0, 32.0) }, motions);
        Assert.Equal(32767u, Assert.Single(pressures));
        Assert.Equal(new uint[] { 100, 101, 102 }, frames);

        var padPresses = new List<(uint Button, uint State)>();
        var padEnters = 0;
        padProxy!.Enter += (_, _) => padEnters++;
        padProxy.Button += (_, e) => padPresses.Add((e.Button, (uint)e.State));
        host.PumpToServer();

        pad.NotifyEnter(tablet, window.ServerSurface);
        pad.NotifyButton(200, 3, pressed: true);
        pad.NotifyButton(210, 3, pressed: false);
        host.PumpUntil(() => padPresses.Count == 2);
        Assert.Equal(1, padEnters);
        Assert.Equal((3u, 1u), padPresses[0]);
        Assert.Equal((3u, 0u), padPresses[1]);

        var removed = false;
        tabletProxy!.Removed += (_, _) => removed = true;
        tablet.Remove();
        host.PumpUntil(() => removed);

        tabletSeat.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void An_injected_tablet_announces_itself_and_its_tool_axes_reach_the_client()
    {
        using var host = new CompositorTestHost();
        var source = host.Backend.CreateTablet();
        using var manager = new TabletManager(host.Display, source);
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.ZwpTabletManagerV2? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_tablet_manager_v2")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwpTabletManagerV2>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var tabletSeat = proxy!.GetTabletSeat(host.Client.Seat!);
        Basin.Desktop.Protocol.ZwpTabletToolV2? toolProxy = null;
        string? tabletName = null;
        tabletSeat.TabletAdded += (_, e) => e.Id.Name += (_, ne) => tabletName = ne.Name;
        tabletSeat.ToolAdded += (_, e) => toolProxy = e.Id;
        host.PumpToServer();

        var tabletInfo = source.AddTablet("Injected Tablet");
        var toolInfo = source.AddTool(TabletToolType.Pen, 0x1234, TabletToolAxis.Pressure | TabletToolAxis.Tilt);
        host.PumpUntil(() => toolProxy is not null && tabletName is not null);
        Assert.Equal("Injected Tablet", tabletName);

        var motions = new List<(double X, double Y)>();
        var pressures = new List<uint>();
        var tilts = new List<(double X, double Y)>();
        var proximityIns = 0;
        var proximityOuts = 0;
        var downs = 0;
        var ups = 0;
        toolProxy!.ProximityIn += (_, _) => proximityIns++;
        toolProxy.ProximityOut += (_, _) => proximityOuts++;
        toolProxy.Motion += (_, e) => motions.Add((e.X.ToDouble(), e.Y.ToDouble()));
        toolProxy.Pressure += (_, e) => pressures.Add(e.Pressure);
        toolProxy.Tilt += (_, e) => tilts.Add((e.TiltX.ToDouble(), e.TiltY.ToDouble()));
        toolProxy.Down += (_, _) => downs++;
        toolProxy.Up += (_, _) => ups++;
        host.PumpToServer();

        manager.ToolProximityIn += (tool, _, axes) =>
            tool.SetFocus(window.ServerSurface, axes.X * 60, axes.Y * 50);
        manager.ToolMoved += (tool, axes) =>
            tool.SetFocus(window.ServerSurface, axes.X * 60, axes.Y * 50);

        source.InjectProximity(toolInfo.Id, tabletInfo.Id, inProximity: true);
        source.InjectAxis(toolInfo.Id, 10, new TabletToolAxes(0.5, 0.5, 0.25, 0, 3, -4, 0, 0, 0));
        source.InjectButton(toolInfo.Id, 11, 0x14a, pressed: true);
        source.InjectAxis(toolInfo.Id, 12, new TabletToolAxes(0.75, 0.25, 0.5, 0, 3, -4, 0, 0, 0));
        source.InjectButton(toolInfo.Id, 13, 0x14a, pressed: false);
        source.InjectProximity(toolInfo.Id, tabletInfo.Id, inProximity: false);
        host.PumpUntil(() => proximityOuts == 1);

        Assert.Equal(1, proximityIns);
        Assert.Equal(1, downs);
        Assert.Equal(1, ups);
        Assert.Contains((30.0, 25.0), motions);
        Assert.Contains((45.0, 12.5), motions);
        Assert.Contains(32767u, pressures);
        Assert.Equal((3.0, -4.0), tilts[0]);

        tabletSeat.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_pads_dials_get_their_own_objects_and_deltas_route_to_the_right_one()
    {
        using var host = new CompositorTestHost();
        var source = host.Backend.CreateTablet();
        using var manager = new TabletManager(host.Display, source);
        var window = MappedToplevel.Map(host, host.Client);

        var tabletSeat = BindSeat(host, TabletManager.Version);
        var dialProxies = new List<Basin.Desktop.Protocol.ZwpTabletPadDialV2>();
        Basin.Desktop.Protocol.ZwpTabletPadV2? padProxy = null;
        tabletSeat.PadAdded += (_, e) =>
        {
            padProxy = e.Id;
            padProxy.Group += (_, ge) => ge.PadGroup.Dial += (_, de) => dialProxies.Add(de.Dial);
        };
        host.PumpToServer();

        var tabletInfo = source.AddTablet("Dialed Tablet");
        var padInfo = source.AddPad(buttons: 4, dials: 2);
        host.PumpUntil(() => dialProxies.Count == 2);

        var first = new List<int>();
        var second = new List<int>();
        var frames = new List<uint>();
        dialProxies[0].Delta += (_, e) => first.Add(e.Value120);
        dialProxies[0].Frame += (_, e) => frames.Add(e.Time);
        dialProxies[1].Delta += (_, e) => second.Add(e.Value120);
        host.PumpToServer();

        var pad = manager.PadFor(padInfo.Id)!;
        var tablet = manager.TabletFor(tabletInfo.Id)!;
        pad.NotifyEnter(tablet, window.ServerSurface);
        source.InjectPad(padInfo.Id, 300, new TabletPadEvent(TabletPadEventKind.Dial, 0, 0, 120, false));
        source.InjectPad(padInfo.Id, 310, new TabletPadEvent(TabletPadEventKind.Dial, 0, 1, -240, false));
        source.InjectPad(padInfo.Id, 320, new TabletPadEvent(TabletPadEventKind.Dial, 0, 0, -30, false));
        host.PumpUntil(() => first.Count == 2 && second.Count == 1);

        Assert.Equal(new[] { 120, -30 }, first);
        Assert.Equal(new[] { -240 }, second);
        Assert.Equal(new uint[] { 300, 320 }, frames);

        source.InjectPad(padInfo.Id, 330, new TabletPadEvent(TabletPadEventKind.Dial, 0, 0, 0, false));
        host.PumpToServer();
        host.PumpToClient();
        Assert.Equal(2, first.Count);

        tabletSeat.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_pad_with_no_dials_announces_none()
    {
        using var host = new CompositorTestHost();
        var source = host.Backend.CreateTablet();
        using var manager = new TabletManager(host.Display, source);

        var tabletSeat = BindSeat(host, TabletManager.Version);
        var dials = 0;
        var groups = 0;
        tabletSeat.PadAdded += (_, e) => e.Id.Group += (_, ge) =>
        {
            groups++;
            ge.PadGroup.Dial += (_, _) => dials++;
        };
        host.PumpToServer();

        source.AddPad(buttons: 4);
        host.PumpUntil(() => groups == 1);
        host.PumpToClient();
        Assert.Equal(0, dials);

        tabletSeat.Dispose();
        host.PumpToServer();
    }

    [Theory]
    [InlineData(3u, 3u)]
    [InlineData(0u, 0u)]
    public void A_tablet_announces_its_bustype_only_when_the_source_knows_one(uint busType, uint expected)
    {
        using var host = new CompositorTestHost();
        var source = host.Backend.CreateTablet();
        using var manager = new TabletManager(host.Display, source);

        var tabletSeat = BindSeat(host, TabletManager.Version);
        uint seen = 0;
        var done = false;
        var bustypeArrivedFirst = false;
        tabletSeat.TabletAdded += (_, e) =>
        {
            e.Id.BustypeEvent += (_, be) =>
            {
                seen = (uint)be.Bustype;
                bustypeArrivedFirst = !done;
            };
            e.Id.Done += (_, _) => done = true;
        };
        host.PumpToServer();

        source.AddTablet("Bus Tablet", busType: busType);
        host.PumpUntil(() => done);
        host.PumpToClient();

        Assert.Equal(expected, seen);
        Assert.Equal(busType != 0, bustypeArrivedFirst);

        tabletSeat.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_client_bound_below_two_sees_no_dials_and_no_bustype()
    {
        using var host = new CompositorTestHost();
        var source = host.Backend.CreateTablet();
        using var manager = new TabletManager(host.Display, source);

        var tabletSeat = BindSeat(host, version: 1);
        var dials = 0;
        var bustypes = 0;
        var done = false;
        tabletSeat.PadAdded += (_, e) => e.Id.Group += (_, ge) => ge.PadGroup.Dial += (_, _) => dials++;
        tabletSeat.TabletAdded += (_, e) =>
        {
            e.Id.BustypeEvent += (_, _) => bustypes++;
            e.Id.Done += (_, _) => done = true;
        };
        host.PumpToServer();

        source.AddTablet("Bus Tablet", busType: 3);
        source.AddPad(buttons: 4, dials: 2);
        host.PumpUntil(() => done);
        host.PumpToClient();

        Assert.Equal(0, dials);
        Assert.Equal(0, bustypes);

        tabletSeat.Dispose();
        host.PumpToServer();
    }

    private static Basin.Desktop.Protocol.ZwpTabletSeatV2 BindSeat(CompositorTestHost host, int version)
    {
        Basin.Desktop.Protocol.ZwpTabletManagerV2? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_tablet_manager_v2")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwpTabletManagerV2>(e.Name, (uint)version);
            }
        };
        host.PumpToClient();
        return proxy!.GetTabletSeat(host.Client.Seat!);
    }
}
