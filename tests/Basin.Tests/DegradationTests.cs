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
    public void Dpms_without_a_backend_reports_unsupported_and_ignores_set()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.DpmsManager(host.Display, power: null);

        var proxy = Bind<Basin.Plasma.Protocol.OrgKdeKwinDpmsManager>(host, "org_kde_kwin_dpms_manager", 1);
        var dpms = proxy.Get(host.Client.Outputs[0]);
        uint? supported = null;
        var modes = new List<uint>();
        var doneCount = 0;
        dpms.Supported += (_, e) => supported = e.Supported;
        dpms.ModeEvent += (_, e) => modes.Add(e.Mode);
        dpms.Done += (_, _) => doneCount++;
        host.PumpUntil(() => doneCount >= 1);

        Assert.Equal(0u, supported);
        Assert.Equal([0u], modes);

        dpms.Set(3);
        host.PumpToServer();
        host.PumpToClient();
        Assert.Equal([0u], modes);
        Assert.Equal(1, doneCount);
        AssertClientAlive(host);
    }

    [Fact]
    public void Kde_idle_without_a_source_binds_and_never_idles()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.KdeIdleManager(host.Display, host.Loop, idle: null);

        var proxy = Bind<Basin.Plasma.Protocol.OrgKdeKwinIdle>(host, "org_kde_kwin_idle", 1);
        var timeout = proxy.GetIdleTimeout(host.Client.Seat!, 10);
        var idled = 0;
        timeout.Idle += (_, _) => idled++;

        for (var i = 0; i < 3; i++)
        {
            host.PumpToServer();
            host.Loop.Dispatch(20);
        }

        host.PumpToClient();
        Assert.Equal(0, idled);

        timeout.SimulateUserActivity();
        host.PumpToServer();
        host.PumpToClient();
        Assert.Equal(0, idled);
        AssertClientAlive(host);
    }

    [Fact]
    public void Keystate_without_a_seat_reports_every_key_unlocked()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.KeyStateManager(host.Display, seat: null);

        var events = new List<(uint Key, uint State)>();
        var proxy = Bind<Basin.Plasma.Protocol.OrgKdeKwinKeystate>(
            host, "org_kde_kwin_keystate", 5, p => p.StateChanged += (_, e) => events.Add((e.Key, e.State)));

        proxy.FetchStates();
        host.PumpToServer();
        host.PumpToClient();

        Assert.Equal(8, events.Count);
        Assert.All(events, e => Assert.Equal(0u, e.State));
        AssertClientAlive(host);
    }

    [Fact]
    public void Fake_input_without_an_authority_or_sink_accepts_and_injects_nothing()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.FakeInputManager(
            host.Display, authority: null, sink: null, seat: host.Seat, layout: host.Layout);

        var proxy = Bind<Basin.Plasma.Protocol.OrgKdeKwinFakeInput>(host, "org_kde_kwin_fake_input", 6);
        proxy.Authenticate("test", "degradation");
        proxy.PointerMotion(WlFixed.FromDouble(5), WlFixed.FromDouble(5));
        proxy.PointerMotionAbsolute(WlFixed.FromDouble(10), WlFixed.FromDouble(10));
        proxy.Button(0x110, 1);
        proxy.Button(0x110, 0);
        proxy.Axis(0, WlFixed.FromDouble(15));
        proxy.KeyboardKey(30, 1);
        proxy.KeyboardKey(30, 0);
        proxy.TouchDown(0, WlFixed.FromDouble(0.5), WlFixed.FromDouble(0.5));
        proxy.TouchMotion(0, WlFixed.FromDouble(0.6), WlFixed.FromDouble(0.6));
        proxy.TouchFrame();
        proxy.TouchUp(0);
        proxy.TouchCancel();
        proxy.KeyboardKeysym(0xffe1, 1);
        host.PumpToServer();

        AssertClientAlive(host);
    }

    [Fact]
    public void Text_input_v2_without_an_input_method_stays_silent()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method: null);

        var window = MappedToplevel.Map(host, host.Client);
        Basin.Plasma.Protocol.ZwpTextInputManagerV2? factory = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_text_input_manager_v2")
            {
                factory = registry.Bind<Basin.Plasma.Protocol.ZwpTextInputManagerV2>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var textInput = factory!.GetTextInput(host.Client.Seat!);
        var entered = 0u;
        var preedits = 0;
        textInput.Enter += (_, e) => entered = e.Serial;
        textInput.PreeditString += (_, _) => preedits++;
        host.PumpToServer();

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();
        Assert.NotEqual(0u, entered);

        textInput.Enable(window.Surface);
        textInput.SetSurroundingText("text", 4, 4);
        textInput.UpdateState_(entered, Basin.Plasma.Protocol.ZwpTextInputV2.UpdateState.Enter);
        host.PumpToServer();
        host.PumpToClient();

        Assert.Equal(0, preedits);
        AssertClientAlive(host);
    }

    [Fact]
    public void Appmenu_without_consumer_wiring_records_and_reports_nothing()
    {
        using var host = new CompositorTestHost();
        var toplevels = new TestToplevelModel();
        using var windows = new PlasmaWindowManager(host.Display, toplevels, workspaces: null);
        using var manager = new Basin.Plasma.AppMenuManager(host.Display, host.Compositor);
        var id = toplevels.Add("Editor", "org.kde.kate");

        var window = MappedToplevel.Map(host, host.Client);
        Basin.Plasma.Protocol.OrgKdeKwinAppmenuManager? factory = null;
        Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement? management = null;
        var announced = new List<(uint Id, string Uuid)>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "org_kde_kwin_appmenu_manager":
                    factory = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinAppmenuManager>(e.Name, 2);
                    break;
                case "org_kde_plasma_window_management":
                    management = registry.Bind<Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement>(e.Name, 20);
                    management.WindowWithUuid += (_, we) => announced.Add((we.Id, we.Uuid));
                    break;
            }
        };
        host.PumpToClient();
        host.PumpToClient();
        var resource = management!.GetWindowByUuid($"basin-{id}");
        var menus = 0;
        resource.ApplicationMenu += (_, _) => menus++;
        host.PumpToServer();

        var appMenu = factory!.Create(window.Surface);
        appMenu.SetAddress("org.kde.kate", "/MenuBar");
        appMenu.Release();
        host.PumpToClient();

        Assert.Equal(0, menus);
        AssertClientAlive(host);
    }

    [Fact]
    public void Decoration_palette_without_a_reader_records_and_survives()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ServerDecorationPaletteManager(host.Display, host.Compositor);

        var window = MappedToplevel.Map(host, host.Client);
        Basin.Plasma.Protocol.OrgKdeKwinServerDecorationPaletteManager? factory = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_server_decoration_palette_manager")
            {
                factory = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinServerDecorationPaletteManager>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var palette = factory!.Create(window.Surface);
        palette.SetPalette("BreezeDark");
        palette.Release();
        host.PumpToServer();

        AssertClientAlive(host);
    }

    [Fact]
    public void Kde_output_management_without_a_backend_fails_the_apply()
    {
        using var host = new CompositorTestHost();
        using var devices = new Basin.Plasma.PlasmaOutputDeviceManager(
            host.Display, host.Layout, outputs: null, configuration: null);
        using var management = new Basin.Plasma.PlasmaOutputManagementManager(
            host.Display, devices, configuration: null);

        var announced = 0;
        var registry = Bind<Basin.Plasma.Protocol.KdeOutputDeviceRegistryV2>(
            host, "kde_output_device_registry_v2", 23, proxy => proxy.Output += (_, _) => announced++);
        host.PumpToClient();
        Assert.Equal(0, announced);

        var proxy = Bind<Basin.Plasma.Protocol.KdeOutputManagementV2>(host, "kde_output_management_v2", 21);
        var configuration = proxy.CreateConfiguration();
        string? reason = null;
        var failed = false;
        configuration.FailureReason += (_, e) => reason = e.Reason;
        configuration.Failed += (_, _) => failed = true;
        configuration.Apply();
        host.PumpUntil(() => failed);

        Assert.Equal("output configuration is not supported", reason);
        AssertClientAlive(host);
    }

    [Fact]
    public void Screencast_without_services_fails_every_stream_kind()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ScreencastManager(host.Display, null, null, null, null, null);

        var proxy = Bind<Basin.Plasma.Protocol.ZkdeScreencastUnstableV1>(host, "zkde_screencast_unstable_v1", 6);
        var failures = new List<string>();
        void Watch(Basin.Plasma.Protocol.ZkdeScreencastStreamUnstableV1 stream) =>
            stream.Failed += (_, e) => failures.Add(e.Error);
        Watch(proxy.StreamOutput(host.Client.Outputs[0], 0));
        Watch(proxy.StreamWindow("basin-1", 0));
        Watch(proxy.StreamRegion(0, 0, 10, 10, Wayland.WlFixed.FromDouble(1), 0));
        Watch(proxy.StreamVirtualOutput("cast", 100, 100, Wayland.WlFixed.FromDouble(1), 0));
        Watch(proxy.StreamVirtualOutputWithDescription("cast", "d", 100, 100, Wayland.WlFixed.FromDouble(1), 0));
        host.PumpUntil(() => failures.Count == 5);

        Assert.All(failures, failure => Assert.False(string.IsNullOrEmpty(failure)));
        AssertClientAlive(host);
    }

    [Fact]
    public void Screencast_with_a_publisher_but_no_capture_fails_only_regions()
    {
        using var host = new CompositorTestHost();
        var publisher = new TestScreencastPublisher();
        using var manager = new Basin.Plasma.ScreencastManager(host.Display, publisher, null, null, null, null);

        var proxy = Bind<Basin.Plasma.Protocol.ZkdeScreencastUnstableV1>(host, "zkde_screencast_unstable_v1", 6);
        var created = 0;
        string? regionFailure = null;
        var output = proxy.StreamOutput(host.Client.Outputs[0], 0);
#pragma warning disable CS0618
        output.Created += (_, _) => created++;
#pragma warning restore CS0618
        var region = proxy.StreamRegion(0, 0, 10, 10, Wayland.WlFixed.FromDouble(1), 0);
        region.Failed += (_, e) => regionFailure = e.Error;
        host.PumpUntil(() => created == 1 && regionFailure is not null);

        Assert.Equal("capture is not available in this session", regionFailure);
        AssertClientAlive(host);
    }

    [Fact]
    public void External_brightness_without_an_output_set_registers_and_asks_nothing()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ExternalBrightnessManager(host.Display, host.Loop, outputs: null);

        var proxy = Bind<Basin.Plasma.Protocol.KdeExternalBrightnessV1>(host, "kde_external_brightness_v1", 3);
        var device = proxy.CreateBrightnessControl();
        var requested = false;
        device.RequestedBrightness += (_, _) => requested = true;
        device.SetInternal(1);
        device.SetEdid(Convert.ToBase64String(new byte[128]));
        device.SetMaxBrightness(100);
        device.SetObservedBrightness(50);
        device.SetUsesDdcCi(1);
        device.Commit();
        host.PumpToClient();

        Assert.False(requested);
        AssertClientAlive(host);
    }

    [Fact]
    public void Kde_output_order_without_an_output_set_sends_done_alone()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.OutputOrderManager(
            host.Display, new LayoutOutputOrder(outputs: null, layout: null));

        var names = 0;
        var done = 0;
        Bind<Basin.Plasma.Protocol.KdeOutputOrderV1>(
            host, "kde_output_order_v1", 1, p =>
            {
                p.Output += (_, _) => names++;
                p.Done += (_, _) => done++;
            });
        host.PumpUntil(() => done >= 1);

        Assert.Equal(0, names);
        Assert.Equal(1, done);
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
    public void Plasma_windows_without_a_stack_report_an_empty_order_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        var toplevels = new TestToplevelModel();
        toplevels.Add("Terminal", "org.foot");
        using var manager = new PlasmaWindowManager(host.Display, toplevels, null);

        var changed2 = 0;
        var deprecated = 0;
        var management = Bind<Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement>(
            host,
            "org_kde_plasma_window_management",
            20,
            proxy =>
            {
                proxy.StackingOrderChanged2 += (_, _) => changed2++;
                proxy.StackingOrderChanged += (_, _) => deprecated++;
                proxy.StackingOrderUuidChanged += (_, _) => deprecated++;
            });

        var order = management.GetStackingOrder();
        var windows = 0;
        var done = false;
        order.Window += (_, _) => windows++;
        order.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Equal(0, windows);
        Assert.Equal(0, changed2);
        Assert.Equal(0, deprecated);
        order.Dispose();
        AssertClientAlive(host);
    }

    [Fact]
    public void Plasma_shell_without_a_scene_tracks_and_places_nothing()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.PlasmaShellManager(host.Display, host.Compositor);

        var proxy = Bind<Basin.Plasma.Protocol.OrgKdePlasmaShell>(host, "org_kde_plasma_shell", 8);
        var surface = host.Client.Compositor.CreateSurface();
        var plasma = proxy.GetSurface(surface);
        plasma.SetRole((uint)Basin.Plasma.Protocol.OrgKdePlasmaSurface.Role.Panel);
        plasma.SetPosition(0, 100);
        var buffer = host.Client.CreateBuffer(160, 20, Fill.Solid(160, 20, 0xFF101010));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 20);
        surface.Commit();
        host.PumpToServer();

        Assert.Single(manager.Surfaces);
        Assert.Equal(Basin.Plasma.PlasmaShellRole.Panel, manager.Surfaces[0].Role);
        AssertClientAlive(host);
    }

    [Fact]
    public void Lockscreen_overlay_with_no_consumer_reading_it_shows_nothing_while_locked()
    {
        using var host = new CompositorTestHost();
        var allowed = new Basin.Plasma.LockOverlaySurfaces();
        using var manager = new Basin.Plasma.LockscreenOverlayManager(host.Display, host.Compositor, allowed);
        var sessionLock = new SessionLockManager(host.Display, host.Compositor);

        var proxy = Bind<Basin.Plasma.Protocol.KdeLockscreenOverlayV1>(host, "kde_lockscreen_overlay_v1", 1);
        var surface = host.Client.Compositor.CreateSurface();
        proxy.Allow(surface);
        host.PumpToServer();

        var windowTree = new Basin.Scene.SceneTree(host.Scene.Root);
        foreach (var scene in host.SurfaceScenes.ToList())
        {
            scene.Destroy();
        }

        _ = new Basin.Scene.SceneSurface(windowTree, host.Compositor.Surfaces.Single());
        var buffer = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0xFF3355FF));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 120);
        surface.Commit();
        host.PumpToServer();

        var locker = host.ConnectClient();
        var lockManager = Bind<Basin.Desktop.Protocol.ExtSessionLockManagerV1>(
            host, "ext_session_lock_manager_v1", 1, client: locker);
        var lockProxy = lockManager.Lock();
        var gotLocked = false;
        lockProxy.Locked += (_, _) => gotLocked = true;
        host.PumpUntil(() => gotLocked);

        windowTree.Enabled = false;
        host.RenderFrame();
        Assert.Equal(0xFF000000u, host.Pixel(80, 60));

        AssertClientAlive(host);
        lockProxy.UnlockAndDestroy();
        host.PumpToServer();
        sessionLock.Dispose();
    }

    [Fact]
    public void Shadow_with_no_effect_wired_tracks_the_buffers_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.ShadowManager(host.Display, host.Compositor);

        var proxy = Bind<Basin.Plasma.Protocol.OrgKdeKwinShadowManager>(host, "org_kde_kwin_shadow_manager", 2);
        var surface = host.Client.Compositor.CreateSurface();
        var shadow = proxy.Create(surface);
        var buffers = new ClientShmBuffer[8];
        for (var i = 0; i < 8; i++)
        {
            buffers[i] = host.Client.CreateBuffer(8, 8, Fill.Solid(8, 8, 0xFF101010));
        }

        shadow.AttachLeft(buffers[0].Proxy);
        shadow.AttachTopLeft(buffers[1].Proxy);
        shadow.AttachTop(buffers[2].Proxy);
        shadow.AttachTopRight(buffers[3].Proxy);
        shadow.AttachRight(buffers[4].Proxy);
        shadow.AttachBottomRight(buffers[5].Proxy);
        shadow.AttachBottom(buffers[6].Proxy);
        shadow.AttachBottomLeft(buffers[7].Proxy);
        shadow.Commit();
        surface.Commit();
        host.PumpToServer();

        var server = host.Compositor.Surfaces.Single();
        Assert.NotNull(manager.ShadowOf(server));

        shadow.Destroy();
        surface.Destroy();
        host.PumpToServer();
        AssertClientAlive(host);
    }

    [Fact]
    public void Slide_with_no_effect_wired_tracks_the_state_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Plasma.SlideManager(host.Display, host.Compositor);

        var proxy = Bind<Basin.Plasma.Protocol.OrgKdeKwinSlideManager>(host, "org_kde_kwin_slide_manager", 1);
        var surface = host.Client.Compositor.CreateSurface();
        var slide = proxy.Create(surface);
        slide.SetLocation((uint)Basin.Plasma.Protocol.OrgKdeKwinSlide.Location.Bottom);
        slide.SetOffset(12);
        slide.Commit();
        surface.Commit();
        host.PumpToServer();

        var server = host.Compositor.Surfaces.Single();
        var tracked = manager.SlideOf(server);
        Assert.NotNull(tracked);
        Assert.Equal(Basin.Plasma.SlideLocation.Bottom, tracked!.Location);
        Assert.Equal(12, tracked.Offset);

        proxy.Unset(surface);
        surface.Commit();
        host.PumpToServer();
        Assert.Null(manager.SlideOf(server));

        slide.Release();
        host.PumpToServer();
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
