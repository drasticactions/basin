using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class DegradationTests
{
    [Fact]
    public void Screencopy_without_a_capture_backend_fails_the_frame()
    {
        using var host = new CompositorTestHost();
        using var manager = new ScreencopyManager(host.Display, host.Layout, host.Buffers, capture: null);

        var proxy = Bind<Basin.Desktop.Protocol.ZwlrScreencopyManagerV1>(host, "zwlr_screencopy_manager_v1", 3);
        var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var announced = false;
        var failed = false;
        frame.Buffer += (_, _) => announced = true;
        frame.Failed += (_, _) => failed = true;
        host.PumpUntil(() => announced);

        var buffer = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0));
        frame.Copy(buffer.Proxy);
        host.PumpUntil(() => failed);

        Assert.True(failed);
        AssertClientAlive(host);
    }

    [Fact]
    public void Image_copy_capture_without_a_backend_stops_the_session()
    {
        using var host = new CompositorTestHost();
        using var sources = new ImageCaptureSourceManager(host.Display);
        using var manager = new ImageCopyCaptureManager(host.Display, host.Buffers, capture: null);

        var captureManager = Bind<Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1>(host, "ext_image_copy_capture_manager_v1", 1);
        var outputSources = Bind<Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1>(
            host, "ext_output_image_capture_source_manager_v1", 1);

        var source = outputSources.CreateSource(host.Client.Outputs[0]);
        var session = captureManager.CreateSession(source, 0);
        var stopped = false;
        session.Stopped += (_, _) => stopped = true;
        host.PumpUntil(() => stopped);

        Assert.True(stopped);
        AssertClientAlive(host);
    }

    [Fact]
    public void Export_dmabuf_without_a_backend_cancels_permanently()
    {
        using var host = new CompositorTestHost();
        using var manager = new ExportDmabufManager(host.Display, capture: null);

        var proxy = Bind<Basin.Desktop.Protocol.ZwlrExportDmabufManagerV1>(host, "zwlr_export_dmabuf_manager_v1", 1);
        var frame = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var reason = -1;
        frame.Cancel += (_, e) => reason = (int)e.Reason;
        host.PumpUntil(() => reason >= 0);

        Assert.Equal((int)Basin.Desktop.Protocol.ZwlrExportDmabufFrameV1.CancelReason.Permanent, reason);
        AssertClientAlive(host);
    }

    [Fact]
    public void Data_control_without_a_clipboard_binds_and_offers_nothing()
    {
        using var host = new CompositorTestHost();
        using var manager = new DataControlManager(host.Display, store: null);

        var proxy = Bind<Basin.Desktop.Protocol.ZwlrDataControlManagerV1>(host, "zwlr_data_control_manager_v1", 2);
        var device = proxy.GetDataDevice(host.Client.Seat!);
        var offers = 0;
        var selections = 0;
        device.DataOffer += (_, _) => offers++;
        device.Selection += (_, _) => selections++;
        host.PumpToClient();
        host.PumpToClient();

        Assert.Equal(0, offers);
        Assert.True(selections >= 1);
        AssertClientAlive(host);
    }

    [Fact]
    public void Gamma_control_without_a_lut_fails_the_control()
    {
        using var host = new CompositorTestHost();
        using var manager = new GammaControlManager(host.Display, gamma: null);

        var proxy = Bind<Basin.Desktop.Protocol.ZwlrGammaControlManagerV1>(host, "zwlr_gamma_control_manager_v1", 1);
        var control = proxy.GetGammaControl(host.Client.Outputs[0]);
        var failed = false;
        control.Failed += (_, _) => failed = true;
        host.PumpUntil(() => failed);

        Assert.True(failed);
        AssertClientAlive(host);
    }

    [Fact]
    public void Output_power_without_a_backend_fails_the_control()
    {
        using var host = new CompositorTestHost();
        using var manager = new OutputPowerManager(host.Display, power: null);

        var proxy = Bind<Basin.Desktop.Protocol.ZwlrOutputPowerManagerV1>(host, "zwlr_output_power_manager_v1", 1);
        var control = proxy.GetOutputPower(host.Client.Outputs[0]);
        var failed = false;
        control.Failed += (_, _) => failed = true;
        host.PumpUntil(() => failed);

        Assert.True(failed);
        AssertClientAlive(host);
    }

    [Fact]
    public void Output_management_without_a_backend_cancels_the_configuration()
    {
        using var host = new CompositorTestHost();
        using var manager = new OutputManagementManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null);

        var proxy = Bind<Basin.Desktop.Protocol.ZwlrOutputManagerV1>(host, "zwlr_output_manager_v1", 2);
        uint serial = 0;
        var heads = 0;
        proxy.Head += (_, _) => heads++;
        proxy.Done += (_, e) => serial = e.Serial;
        host.PumpUntil(() => serial != 0);

        Assert.True(heads >= 1);

        var configuration = proxy.CreateConfiguration(serial);
        var canceled = false;
        configuration.Cancelled += (_, _) => canceled = true;
        configuration.Test();
        host.PumpUntil(() => canceled);

        Assert.True(canceled);
        AssertClientAlive(host);
    }

    [Fact]
    public void Drm_lease_without_a_device_offers_no_connectors_and_finishes_the_lease()
    {
        using var host = new CompositorTestHost();
        using var manager = new DrmLeaseManager(host.Display, device: null);

        var proxy = Bind<Basin.Desktop.Protocol.WpDrmLeaseDeviceV1>(host, "wp_drm_lease_device_v1", 1);
        var connectors = 0;
        var done = false;
        proxy.Connector += (_, _) => connectors++;
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Equal(0, connectors);

        var request = proxy.CreateLeaseRequest();
        var lease = request.Submit();
        var finished = false;
        lease.Finished += (_, _) => finished = true;
        host.PumpUntil(() => finished);

        Assert.True(finished);
        AssertClientAlive(host);
    }

    [Fact]
    public void Foreign_toplevel_without_a_model_finishes_the_list()
    {
        using var host = new CompositorTestHost();
        using var list = new ForeignToplevelListManager(host.Display, model: null);
        using var management = new ForeignToplevelManager(host.Display, model: null);

        var listFinished = false;
        var managerFinished = false;
        var listProxy = Bind<Basin.Desktop.Protocol.ExtForeignToplevelListV1>(
            host, "ext_foreign_toplevel_list_v1", 1, p => p.Finished += (_, _) => listFinished = true);
        var managerProxy = Bind<Basin.Desktop.Protocol.ZwlrForeignToplevelManagerV1>(
            host, "zwlr_foreign_toplevel_manager_v1", 3, p => p.Finished += (_, _) => managerFinished = true);
        host.PumpUntil(() => listFinished && managerFinished);

        Assert.True(listFinished);
        Assert.True(managerFinished);
        AssertClientAlive(host);
    }

    [Fact]
    public void Workspaces_without_a_model_report_done_with_no_groups()
    {
        using var host = new CompositorTestHost();
        using var manager = new WorkspaceManager(host.Display, Capabilities.EmptyWorkspaceModel.Instance);

        var proxy = Bind<Basin.Desktop.Protocol.ExtWorkspaceManagerV1>(host, "ext_workspace_manager_v1", 1);
        var groups = 0;
        var done = false;
        proxy.WorkspaceGroup += (_, _) => groups++;
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Equal(0, groups);
        AssertClientAlive(host);
    }

    [Fact]
    public void Cursor_shape_without_a_theme_accepts_the_request_and_changes_nothing()
    {
        using var host = new CompositorTestHost();
        using var manager = new CursorShapeManager(host.Display, theme: null);
        var shapes = new List<CursorShape>();
        var resolved = 0;
        manager.ShapeRequested += shapes.Add;
        manager.CursorRequested += _ => resolved++;

        var proxy = Bind<Basin.Desktop.Protocol.WpCursorShapeManagerV1>(host, "wp_cursor_shape_manager_v1", 1);
        var pointer = host.Client.Seat!.GetPointer();
        var device = proxy.GetPointer(pointer);
        device.SetShape(0, Basin.Desktop.Protocol.WpCursorShapeDeviceV1.Shape.Grab);
        host.PumpUntil(() => shapes.Count == 1);

        Assert.Equal(CursorShape.Grab, Assert.Single(shapes));
        Assert.Equal(0, resolved);
        AssertClientAlive(host);
    }

    [Fact]
    public void Virtual_input_without_a_sink_accepts_and_discards()
    {
        using var host = new CompositorTestHost();
        using var keyboard = new VirtualKeyboardManager(host.Display, sink: null);
        using var pointer = new VirtualPointerManager(host.Display, sink: null);

        var keyboardProxy = Bind<Basin.Desktop.Protocol.ZwpVirtualKeyboardManagerV1>(host, "zwp_virtual_keyboard_manager_v1", 1);
        var pointerProxy = Bind<Basin.Desktop.Protocol.ZwlrVirtualPointerManagerV1>(host, "zwlr_virtual_pointer_manager_v1", 2);
        var virtualKeyboard = keyboardProxy.CreateVirtualKeyboard(host.Client.Seat!);
        var virtualPointer = pointerProxy.CreateVirtualPointer(host.Client.Seat);
        virtualKeyboard.Key(1, 30, 1);
        virtualPointer.Motion(1, WlFixed.FromDouble(2), WlFixed.FromDouble(3));
        virtualPointer.Frame();
        host.PumpToServer();
        host.PumpToClient();

        AssertClientAlive(host);
        virtualKeyboard.Dispose();
        virtualPointer.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Text_input_without_a_method_still_delivers_focus()
    {
        using var host = new CompositorTestHost();
        using var manager = new TextInputManager(host.Display, host.Seat, method: null);
        var window = MappedToplevel.Map(host, host.Client);

        var proxy = Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(host, "zwp_text_input_manager_v3", 1);
        var textInput = proxy.GetTextInput(host.Client.Seat!);
        var entered = 0;
        var preedits = 0;
        textInput.Enter += (_, _) => entered++;
        textInput.PreeditString += (_, _) => preedits++;

        manager.NotifyFocus(window.ServerSurface);
        host.PumpUntil(() => entered == 1);
        textInput.Enable();
        textInput.Commit();
        host.PumpToServer();
        host.PumpToClient();

        Assert.Equal(1, entered);
        Assert.Equal(0, preedits);
        AssertClientAlive(host);
    }

    [Fact]
    public void Text_input_v1_without_a_method_still_delivers_focus()
    {
        using var host = new CompositorTestHost();
        using var manager = new TextInputV1Manager(host.Display, host.Seat, method: null);
        var window = MappedToplevel.Map(host, host.Client);

        var proxy = Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV1>(host, "zwp_text_input_manager_v1", 1);
        var textInput = proxy.CreateTextInput();
        var entered = 0;
        var left = 0;
        var preedits = 0;
        textInput.Enter += (_, _) => entered++;
        textInput.Leave += (_, _) => left++;
        textInput.PreeditString += (_, _) => preedits++;

        textInput.Activate(host.Client.Seat!, window.Surface);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        textInput.SetSurroundingText("hello", 5, 5);
        textInput.CommitState(1);
        host.PumpUntil(() => entered == 1);

        host.Seat.Keyboard.NotifyEnter(null);
        host.PumpUntil(() => left == 1);

        Assert.Equal(0, preedits);
        AssertClientAlive(host);
        textInput.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_bell_without_an_implementation_accepts_the_ring()
    {
        using var host = new CompositorTestHost();
        using var manager = new SystemBellManager(host.Display, host.Compositor, bell: null);
        var rang = 0;
        manager.Rang += _ => rang++;

        var proxy = Bind<Basin.Desktop.Protocol.XdgSystemBellV1>(host, "xdg_system_bell_v1", 1);
        proxy.Ring(null);
        host.PumpUntil(() => rang == 1);

        AssertClientAlive(host);
    }

    [Fact]
    public void Tablet_without_a_source_announces_an_empty_seat()
    {
        using var host = new CompositorTestHost();
        using var manager = new TabletManager(host.Display, source: null);

        var proxy = Bind<Basin.Desktop.Protocol.ZwpTabletManagerV2>(host, "zwp_tablet_manager_v2", 1);
        var seat = proxy.GetTabletSeat(host.Client.Seat!);
        var devices = 0;
        seat.TabletAdded += (_, _) => devices++;
        seat.ToolAdded += (_, _) => devices++;
        seat.PadAdded += (_, _) => devices++;
        host.PumpToClient();

        Assert.Equal(0, devices);
        AssertClientAlive(host);
        seat.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Session_management_without_a_store_restores_nothing()
    {
        using var host = new CompositorTestHost();
        using var manager = new SessionManager(host.Display, store: null);

        var proxy = Bind<Basin.Desktop.Protocol.XdgSessionManagerV1>(host, "xdg_session_manager_v1", 1);
        var created = 0;
        var restored = 0;

        var session = proxy.GetSession(Basin.Desktop.Protocol.XdgSessionManagerV1.Reason.Launch, null);
        session.Created += (_, _) => created++;
        session.Restored += (_, _) => restored++;

        var surface = host.Client.Compositor.CreateSurface();
        var xdgSurface = host.Client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        xdgSurface.Configure += (_, e) => xdgSurface.AckConfigure(e.Serial);

        var handle = session.RestoreToplevel(toplevel, "main");
        var toplevelRestored = 0;
        handle.Restored += (_, _) => toplevelRestored++;
        surface.Commit();
        host.PumpToClient();
        host.PumpToClient();

        Assert.Equal(0, created);
        Assert.Equal(0, restored);
        Assert.Equal(0, toplevelRestored);
        AssertClientAlive(host);
    }

    [Fact]
    public void Toplevel_drag_without_a_drag_tracker_attaches_nothing()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Shell.Xdg.XdgToplevelDragManager(host.Display, drags: null);
        var window = MappedToplevel.Map(host, host.Client);

        var proxy = Bind<Basin.Shell.Xdg.Protocol.XdgToplevelDragManagerV1>(host, "xdg_toplevel_drag_manager_v1", 1);
        var source = host.Client.DataDeviceManager!.CreateDataSource();
        var drag = proxy.GetXdgToplevelDrag(source);
        drag.Attach(window.Toplevel, 3, 4);
        host.PumpToClient();

        Assert.Null(manager.Attachment);
        AssertClientAlive(host);

        drag.Dispose();
        source.Dispose();
        AssertClientAlive(host);
    }

    [Fact]
    public void Background_effect_without_a_renderer_advertises_no_capabilities()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, effects: null);
        var window = MappedToplevel.Map(host, host.Client);

        uint? capabilities = null;
        var proxy = Bind<Basin.Desktop.Protocol.ExtBackgroundEffectManagerV1>(
            host, "ext_background_effect_manager_v1", 1,
            p => p.Capabilities += (_, e) => capabilities = (uint)e.Flags);

        host.PumpUntil(() => capabilities is not null);
        Assert.Equal(0u, capabilities);

        var effect = proxy.GetBackgroundEffect(window.Surface);
        var region = host.Client.Compositor.CreateRegion();
        region.Add(0, 0, 10, 10);
        effect.SetBlurRegion(region);
        region.Destroy();
        window.Surface.Commit();
        host.PumpToServer();

        Assert.NotNull(manager.BlurRegionOf(window.ServerSurface));
        AssertClientAlive(host);
    }

    [Fact]
    public void Fullscreen_shell_without_an_output_presents_nothing()
    {
        using var host = new CompositorTestHost();
        var empty = new OutputLayout();
        using var shell = new FullscreenShellGlobal(host.Display, host.Compositor, empty);

        var proxy = Bind<Basin.Desktop.Protocol.ZwpFullscreenShellV1>(host, "zwp_fullscreen_shell_v1", 1);
        var surface = host.Client.Compositor.CreateSurface();
        proxy.PresentSurface(
            surface, Basin.Desktop.Protocol.ZwpFullscreenShellV1.PresentMethod.Default, output: null);
        host.PumpToServer();

        Assert.Null(shell.PresentedSurface);
        Assert.Single(shell.BoundClients);
        AssertClientAlive(host);
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

    private static T Bind<T>(
        CompositorTestHost host, string wireInterface, uint version, Action<T>? wire = null,
        ShmTestClient? client = null)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = (client ?? host.Client).Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, version);

                wire?.Invoke(proxy);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}
