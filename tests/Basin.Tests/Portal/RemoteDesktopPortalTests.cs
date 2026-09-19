using Basin.Capabilities;
using Basin.Portal;
using Basin.Tests.PortalClient;
using Libei;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class RemoteDesktopPortalTests
{
    private const string Impl = "org.freedesktop.impl.portal.RemoteDesktop";
    private const string CastImpl = "org.freedesktop.impl.portal.ScreenCast";

    private static BasinServices Services(CompositorTestHost host, PortalBus bus, IInputSink? sink, IPortalPrompts prompts, Action<BasinServices>? more = null)
    {
        var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use<IScreenCapture>(new TestScreenCapture(host))
            .Use<IScreencastPublisher>(new TestScreencastPublisher())
            .Use<ISelectionStore>(new Basin.Seat.SeatSelectionStore(host.Seat))
            .Use(prompts);
        if (sink is not null)
        {
            services.Use(sink);
        }

        more?.Invoke(services);
        return services.Install(PortalPack.Default.Without("org.freedesktop.impl.portal.Screenshot").Without("org.freedesktop.impl.portal.InputCapture").Without("org.freedesktop.impl.portal.GlobalShortcuts")).Freeze();
    }

    private static ObjectPath NewSession(PortalTestClient frontend, CompositorTestHost host, int n = 1)
    {
        var handle = new ObjectPath(PortalBus.RootPath + "/session/1_1/rd" + n);
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "CreateSession", handle, "org.example.App", []));
        Assert.Equal(0u, response);
        return handle;
    }

    private static void Start(PortalTestClient frontend, CompositorTestHost host, ObjectPath session, uint expectedDevices, Dictionary<string, VariantValue>? select = null)
    {
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectDevices", session, "org.example.App", select ?? []));
        Assert.Equal(0u, response);
        Dictionary<string, VariantValue> results;
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", session, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Equal(expectedDevices, results["devices"].GetUInt32());
    }

    [Fact(Skip = EisAvailability.Missing, SkipType = typeof(EisAvailability), SkipUnless = nameof(EisAvailability.Loaded))]
    public void A_remote_desktop_session_through_the_distro_frontend_injects_and_connects_eis()
    {
        PortalFrontendFixture.SkipUnlessAvailable();
        using var host = new CompositorTestHost(64, 48);
        using var fixture = PortalFrontendFixture.Start(Impl, CastImpl);
        var sink = new RecordingInputSink();
        var prompts = new AutoAnswerPrompts();
        using var bus = new PortalBus(host.Loop, fixture.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, sink, prompts);
        PortalBusTests.Await(host, bus.Started);
        fixture.StartFrontend(host, bus);

        using var client = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(fixture.Address, PortalBus.FrontendName, asFrontend: false));
        var remote = new RemoteDesktop(client.Connection, PortalBus.FrontendName, new ObjectPath(PortalBus.RootPath));
        Assert.Equal(2u, PortalBusTests.Await(host, remote.GetVersionAsync()));
        Assert.Equal(3u, PortalBusTests.Await(host, remote.GetAvailableDeviceTypesAsync()));

        var sessionOptions = new Dictionary<string, VariantValue>();
        var sessionPath = PortalClientRequests.SessionToken(client.Connection, sessionOptions);
        Assert.Equal(0u, PortalClientRequests.Run(host, client.Connection, o => remote.CreateSessionAsync(o), sessionOptions).Response);
        Assert.Equal(0u, PortalClientRequests.Run(host, client.Connection, o => remote.SelectDevicesAsync(sessionPath, o), new Dictionary<string, VariantValue>
        {
            ["types"] = VariantValue.UInt32(3),
        }).Response);
        var started = PortalClientRequests.Run(host, client.Connection, o => remote.StartAsync(sessionPath, "", o));
        Assert.True(started.Response == 0, "response " + started.Response + "\n" + fixture.Dump());
        Assert.Equal(3u, started.Results["devices"].GetUInt32());
        var prompt = Assert.IsType<DevicePrompt>(Assert.Single(prompts.Asked));
        Assert.Equal(InputDeviceCapability.Keyboard | InputDeviceCapability.Pointer, prompt.Requested);

        PortalBusTests.Await(host, remote.NotifyPointerMotionAsync(sessionPath, [], 3, -2));
        PortalBusTests.Await(host, remote.NotifyPointerButtonAsync(sessionPath, [], (int)InputCodes.BtnLeft, 1));
        PortalBusTests.Await(host, remote.NotifyKeyboardKeycodeAsync(sessionPath, [], 30, 1));
        PortalBusTests.Await(host, remote.NotifyKeyboardKeycodeAsync(sessionPath, [], 30, 0));
        PortalBusTests.Await(host, remote.NotifyPointerAxisDiscreteAsync(sessionPath, [], 0, 2));
        PortalBusTests.PumpUntil(host, () => sink.Axes.Count == 1);
        Assert.Equal([(3.0, -2.0)], sink.Motions.Select(m => (m.Dx, m.Dy)));
        Assert.Equal([(InputCodes.BtnLeft, true)], sink.Buttons.Select(b => (b.Button, b.Pressed)));
        Assert.Equal([(30u, true), (30u, false)], sink.Keys.Select(k => (k.Key, k.Pressed)));
        Assert.Equal([(0u, 30.0)], sink.Axes.Select(a => (a.Axis, a.Value)));

        using var eisHandle = PortalBusTests.Await(host, remote.ConnectToEISAsync(sessionPath, []));
        Assert.False(eisHandle.IsInvalid);
        using var ei = EiContext.CreateSender("basin-test");
        var eisFd = (int)eisHandle.DangerousGetHandle();
        eisHandle.SetHandleAsInvalid();
        ei.ConnectToFd(eisFd);
        EiDevice? pointer = null;
        for (var round = 0; round < 20 && pointer is null; round++)
        {
            host.Loop.Dispatch(5);
            ei.Dispatch();
            while (ei.TryGetEvent(out var @event))
            {
                using (@event)
                {
                    if (@event.Type == EiEventType.SeatAdded)
                    {
                        using var seat = @event.GetSeat();
                        seat!.BindCapabilities(EiDeviceCapability.Pointer | EiDeviceCapability.Button | EiDeviceCapability.Scroll);
                    }
                    else if (@event.Type == EiEventType.DeviceAdded)
                    {
                        var device = @event.GetDevice()!;
                        if (device.HasCapability(EiDeviceCapability.Pointer))
                        {
                            pointer = device;
                        }
                        else
                        {
                            device.Dispose();
                        }
                    }
                }
            }
        }

        Assert.NotNull(pointer);
        pointer.StartEmulating(1);
        pointer.PointerMotion(7, 1);
        pointer.Frame(0);
        PortalBusTests.PumpUntil(host, () =>
        {
            ei.Dispatch();
            while (ei.TryGetEvent(out var e))
            {
                e.Dispose();
            }

            return sink.Motions.Count == 2;
        });
        Assert.Equal((7.0, 1.0), (sink.Motions[1].Dx, sink.Motions[1].Dy));
        pointer.Dispose();
        ei.Disconnect();

        var session = new PortalClient.Session(client.Connection, PortalBus.FrontendName, sessionPath);
        PortalBusTests.Await(host, session.CloseAsync());
        PortalBusTests.PumpUntil(host, () => bus.Sessions.Count == 0);
        Assert.Equal([(InputCodes.BtnLeft, true), (InputCodes.BtnLeft, false)], sink.Buttons.Select(b => (b.Button, b.Pressed)));
    }

    [Fact]
    public void A_combined_session_maps_absolute_motion_onto_its_stream()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        var second = host.Backend.CreateOutput(new OutputMode(32, 24, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 64, 0);
        var sink = new RecordingInputSink();
        var prompts = new AutoAnswerPrompts { Sources = AutoAnswerPrompts.SourceAnswer.AllOutputs };
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, sink, prompts, s => s.Use<IAppInfoResolver>(new StubAppInfo()));
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var session = NewSession(frontend, host);
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(CastImpl, "SelectSources", session, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["multiple"] = VariantValue.Bool(true),
        }));
        Assert.Equal(0u, response);
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectDevices", session, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["types"] = VariantValue.UInt32(2),
            ["persist_mode"] = VariantValue.UInt32(2),
        }));
        Assert.Equal(0u, response);
        prompts.PersistMode = 2;
        Dictionary<string, VariantValue> results;
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", session, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Equal(2u, results["devices"].GetUInt32());
        Assert.False(results["clipboard_enabled"].GetBool());
        Assert.Equal(2, results["streams"].Count);
        Assert.Equal(2, prompts.Asked.Count);
        var devicePrompt = Assert.IsType<DevicePrompt>(prompts.Asked[0]);
        Assert.Equal(("Stub App", "/nonexistent/stub.png"), (devicePrompt.DisplayName, devicePrompt.IconPath));
        var sourcePrompt = Assert.IsType<SourcePrompt>(prompts.Asked[1]);
        Assert.Equal("Stub App", sourcePrompt.DisplayName);
        var restore = results["restore_data"];
        var farNode = results["streams"].GetItem(1).GetItem(0).GetUInt32();

        PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "NotifyPointerMotionAbsolute", "oa{sv}udd", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteDictionary(new Dictionary<string, VariantValue>());
            w.WriteUInt32(farNode);
            w.WriteDouble(10);
            w.WriteDouble(5);
        }));
        PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "NotifyKeyboardKeycode", "oa{sv}iu", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteDictionary(new Dictionary<string, VariantValue>());
            w.WriteInt32(30);
            w.WriteUInt32(1);
        }));
        PortalBusTests.PumpUntil(host, () => sink.AbsoluteMotions.Count == 1);
        Assert.Equal((74.0, 5.0, 96.0, 48.0), (sink.AbsoluteMotions[0].X, sink.AbsoluteMotions[0].Y, sink.AbsoluteMotions[0].Width, sink.AbsoluteMotions[0].Height));
        Assert.Empty(sink.Keys);

        var again = NewSession(frontend, host, n: 2);
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(CastImpl, "SelectSources", again, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["multiple"] = VariantValue.Bool(true),
        }));
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectDevices", again, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["types"] = VariantValue.UInt32(2),
            ["persist_mode"] = VariantValue.UInt32(2),
            ["restore_data"] = restore,
        }));
        Assert.Equal(0u, response);
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", again, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Equal(2, results["streams"].Count);
        Assert.Equal(2, prompts.Asked.Count);
    }

    [Fact]
    public void Keysyms_resolve_through_the_seat_keymap_and_refused_devices_inject_nothing()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        host.Seat.Keyboard.SetKeymap(new KeymapNames(Layout: "us"));
        Assert.SkipWhen(host.Seat.Keyboard.Keymap is null, "no xkb keymap could be compiled on this host");
        var sink = new RecordingInputSink();
        var prompts = new AutoAnswerPrompts { Devices = InputDeviceCapability.Keyboard };
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, sink, prompts, s => s.Use<IKeymapLookup>(host.Seat.Keyboard));
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var session = NewSession(frontend, host);
        Start(frontend, host, session, expectedDevices: 1);
        PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "NotifyKeyboardKeysym", "oa{sv}iu", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteDictionary(new Dictionary<string, VariantValue>());
            w.WriteInt32((int)Basin.Config.Keysym.FromName("A"));
            w.WriteUInt32(1);
        }));
        PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "NotifyKeyboardKeysym", "oa{sv}iu", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteDictionary(new Dictionary<string, VariantValue>());
            w.WriteInt32((int)Basin.Config.Keysym.FromName("A"));
            w.WriteUInt32(0);
        }));
        PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "NotifyPointerMotion", "oa{sv}dd", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteDictionary(new Dictionary<string, VariantValue>());
            w.WriteDouble(1);
            w.WriteDouble(1);
        }));
        PortalBusTests.PumpUntil(host, () => sink.Keys.Count >= 4);
        Assert.Equal([(42u, true), (30u, true), (30u, false), (42u, false)], sink.Keys.Select(k => (k.Key, k.Pressed)));
        Assert.Empty(sink.Motions);
    }

    [Fact]
    public void Without_an_input_sink_nothing_is_offered_and_start_answers_other()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, null, new AutoAnswerPrompts());
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));
        Assert.Equal(0u, PortalBusTests.Await(host, frontend.GetPropertyAsync(PortalBus.RootPath, Impl, "AvailableDeviceTypes")).GetUInt32());

        var session = NewSession(frontend, host);
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectDevices", session, "org.example.App", []));
        Assert.Equal(0u, response);
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", session, "org.example.App", [], parentWindow: ""));
        Assert.Equal(2u, response);
        Assert.Single(bus.Sessions);
    }

    private sealed class StubAppInfo : IAppInfoResolver
    {
        public bool TryResolve(string appId, out AppInfo info)
        {
            info = new AppInfo("Stub App", "/nonexistent/stub.png");
            return appId.Length > 0;
        }
    }
}
