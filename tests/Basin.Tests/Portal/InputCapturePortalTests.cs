using Basin.Capabilities;
using Basin.Eis;
using Basin.Hypr.InputCapture;
using Basin.Portal;
using Basin.Tests.PortalClient;
using Libei;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class InputCapturePortalTests
{
    private const string Impl = "org.freedesktop.impl.portal.InputCapture";

    private static (PortalBus Bus, BasinServices Services, InputCaptureEngine Engine) Setup(CompositorTestHost host, string address, AutoAnswerPrompts prompts, bool withSelectionStore = false)
    {
        var engine = new InputCaptureEngine(host.Loop, host.Layout, host.Seat);
        var bus = new PortalBus(host.Loop, address, PortalBusTests.BusName);
        var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use(engine)
            .Use<Basin.Portal.IInputCaptureProvider>(new Basin.Portal.EngineInputCaptureProvider(engine))
            .Use<IPortalPrompts>(prompts);
        if (withSelectionStore)
        {
            services.Use<ISelectionStore>(new Basin.Seat.SeatSelectionStore(host.Seat));
        }

        var pack = new ProtocolPack([new PortalInputCaptureModule(), new PortalClipboardModule()]);
        services.Install(withSelectionStore ? pack : pack.Without("org.freedesktop.impl.portal.Clipboard")).Freeze();
        PortalBusTests.Await(host, bus.Started);
        return (bus, services, engine);
    }

    private static ObjectPath StartedSession(PortalTestClient frontend, CompositorTestHost host, string name = "ic1", uint capabilities = 3)
    {
        var handle = new ObjectPath(PortalBus.RootPath + "/session/1_1/" + name);
        _ = PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "CreateSession2", "osa{sv}", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(handle);
            w.WriteString("org.example.App");
            w.WriteDictionary(new Dictionary<string, VariantValue>());
        }, static (Message m, object? _) => m.GetBodyReader().ReadDictionaryOfStringToVariantValue()));
        var (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", handle, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["capabilities"] = VariantValue.UInt32(capabilities),
        }, parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Equal(capabilities, results["capabilities"].GetUInt32());
        return handle;
    }

    private static (uint Response, Dictionary<string, VariantValue> Results) Zones(PortalTestClient frontend, CompositorTestHost host, ObjectPath session) =>
        PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "GetZones", session, "org.example.App", []));

    private static (uint Response, Dictionary<string, VariantValue> Results) Barriers(PortalTestClient frontend, CompositorTestHost host, ObjectPath session, uint zoneSet, params (uint Id, int X1, int Y1, int X2, int Y2)[] barriers) =>
        PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "SetPointerBarriers", "oosa{sv}aa{sv}u", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(frontend.NextRequest());
            w.WriteObjectPath(session);
            w.WriteString("org.example.App");
            w.WriteDictionary(new Dictionary<string, VariantValue>());
            var array = w.WriteArrayStart(DBusType.Array);
            foreach (var (id, x1, y1, x2, y2) in barriers)
            {
                w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["barrier_id"] = VariantValue.UInt32(id),
                    ["position"] = VariantValue.Struct(VariantValue.Int32(x1), VariantValue.Int32(y1), VariantValue.Int32(x2), VariantValue.Int32(y2)),
                });
            }

            w.WriteArrayEnd(array);
            w.WriteUInt32(zoneSet);
        }, static (Message m, object? _) => PortalTestClient.ReadResponse(m)));

    private static Task SessionCall(PortalTestClient frontend, ObjectPath session, string member, Dictionary<string, VariantValue>? options = null) =>
        frontend.CallRawAsync(Impl, member, "osa{sv}", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteString("org.example.App");
            w.WriteDictionary(options ?? []);
        }, static (Message m, object? _) => PortalTestClient.ReadResponse(m));

    [Fact]
    public void Zones_barriers_activation_and_release_follow_the_engine()
    {
        Assert.SkipUnless(EisLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(160, 120);
        var prompts = new AutoAnswerPrompts();
        var (bus, services, engine) = Setup(host, daemon.Address, prompts);
        using (bus)
        using (services)
        using (engine)
        {
            using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));
            Assert.Equal(3u, PortalBusTests.Await(host, frontend.GetPropertyAsync(PortalBus.RootPath, Impl, "SupportedCapabilities")).GetUInt32());
            var session = StartedSession(frontend, host);
            Assert.IsType<ConfirmPrompt>(Assert.Single(prompts.Asked));
            Assert.Equal(1, engine.SessionCount);

            var (response, results) = Zones(frontend, host, session);
            Assert.Equal(0u, response);
            var zones = results["zones"];
            Assert.Equal(1, zones.Count);
            var zone = zones.GetItem(0);
            Assert.Equal((160u, 120u, 0, 0), (zone.GetItem(0).GetUInt32(), zone.GetItem(1).GetUInt32(), zone.GetItem(2).GetInt32(), zone.GetItem(3).GetInt32()));
            var zoneSet = results["zone_set"].GetUInt32();

            (response, results) = Barriers(frontend, host, session, zoneSet, (9, 0, 0, 0, 119), (10, 5, 5, 40, 40));
            Assert.Equal(0u, response);
            Assert.Equal([10u], results["failed_barriers"].GetArray<uint>());

            (response, results) = Barriers(frontend, host, session, zoneSet + 1, (11, 160, 0, 160, 119));
            Assert.Equal(0u, response);
            Assert.Equal([11u], results["failed_barriers"].GetArray<uint>());
            (response, _) = Barriers(frontend, host, session, zoneSet, (9, 0, 0, 0, 119));

            var events = new List<(string Member, uint Activation, double X, double Y, uint Barrier)>();
            foreach (var member in new[] { "Activated", "Deactivated", "Disabled" })
            {
                var name = member;
                using var watch = PortalBusTests.Await(host, frontend.Connection.WatchSignalAsync<(ObjectPath, Dictionary<string, VariantValue>)>(
                    PortalBusTests.BusName, PortalBus.RootPath, Impl, name,
                    static (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPath(), r.ReadDictionaryOfStringToVariantValue()); },
                    (Notification<(ObjectPath, Dictionary<string, VariantValue>)> n) =>
                    {
                        if (!n.HasValue)
                        {
                            return;
                        }

                        var o = n.Value.Item2;
                        var position = o.TryGetValue("cursor_position", out var p) ? (p.GetItem(0).GetDouble(), p.GetItem(1).GetDouble()) : (0, 0);
                        events.Add((name, o.TryGetValue("activation_id", out var a) ? a.GetUInt32() : 0, position.Item1, position.Item2, o.TryGetValue("barrier_id", out var b) ? b.GetUInt32() : 0));
                    },
                    ObserverFlags.None, emitOnCapturedContext: false).AsTask());

                if (member == "Activated")
                {
                    PortalBusTests.Await(host, SessionCall(frontend, session, "Enable"));
                    Assert.False(engine.NotifyMotion(1, 5, 50, -2, 0));
                    Assert.True(engine.NotifyMotion(2, -3, 50, -8, 0));
                    Assert.True(engine.IsCaptured);
                    PortalBusTests.PumpUntil(host, () => events.Count == 1);
                    Assert.Equal(("Activated", 1u, -3.0, 50.0, 9u), events[0]);
                }
                else if (member == "Deactivated")
                {
                    var warps = new List<(double X, double Y)>();
                    engine.WarpRequested += (x, y) => warps.Add((x, y));
                    PortalBusTests.Await(host, SessionCall(frontend, session, "Release", new Dictionary<string, VariantValue>
                    {
                        ["activation_id"] = VariantValue.UInt32(1),
                        ["cursor_position"] = VariantValue.Struct(VariantValue.Double(40), VariantValue.Double(30)),
                    }));
                    Assert.False(engine.IsCaptured);
                    Assert.Equal([(40.0, 30.0)], warps);
                    PortalBusTests.PumpUntil(host, () => events.Count == 2);
                    Assert.Equal(("Deactivated", 1u), (events[1].Member, events[1].Activation));
                }
                else
                {
                    PortalBusTests.Await(host, SessionCall(frontend, session, "Disable"));
                    PortalBusTests.PumpUntil(host, () => events.Count == 3);
                    Assert.Equal("Disabled", events[2].Member);
                }
            }

            PortalBusTests.Await(host, frontend.CallAsync(session.ToString(), "org.freedesktop.impl.portal.Session", "Close"));
            Assert.Equal(0, engine.SessionCount);
        }
    }

    [Fact]
    public void Eis_input_arrives_while_captured_and_the_hyprland_front_is_excluded()
    {
        Assert.SkipUnless(EisLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(160, 120);
        var (bus, services, engine) = Setup(host, daemon.Address, new AutoAnswerPrompts());
        using (bus)
        using (services)
        using (engine)
        using (var hypr = new HyprlandInputCaptureManager(host.Display, engine))
        {
            using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));
            var session = StartedSession(frontend, host);
            using var handle = PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "ConnectToEIS", "osa{sv}", (ref MessageWriter w) =>
            {
                w.WriteObjectPath(session);
                w.WriteString("org.example.App");
                w.WriteDictionary(new Dictionary<string, VariantValue>());
            }, static (Message m, object? _) => m.GetBodyReader().ReadHandle<Microsoft.Win32.SafeHandles.SafeFileHandle>()!));

            using var ei = EiContext.CreateReceiver("basin-test");
            var eisFd = (int)handle.DangerousGetHandle();
            handle.SetHandleAsInvalid();
            ei.ConnectToFd(eisFd);
            var motions = new List<(double Dx, double Dy)>();
            var devices = 0;
            void PumpEi()
            {
                for (var round = 0; round < 10; round++)
                {
                    host.Loop.Dispatch(0);
                    ei.Dispatch();
                    while (ei.TryGetEvent(out var @event))
                    {
                        using (@event)
                        {
                            switch (@event.Type)
                            {
                                case EiEventType.SeatAdded:
                                    using (var seat = @event.GetSeat())
                                    {
                                        seat!.BindCapabilities(EiDeviceCapability.Pointer | EiDeviceCapability.Button | EiDeviceCapability.Scroll | EiDeviceCapability.Keyboard);
                                    }

                                    break;
                                case EiEventType.DeviceAdded:
                                    devices++;
                                    break;
                                case EiEventType.PointerMotion:
                                    var motion = (EiPointerMotionEvent)@event;
                                    motions.Add((motion.Dx, motion.Dy));
                                    break;
                            }
                        }
                    }
                }
            }

            PumpEi();
            Assert.Equal(2, devices);
            var (_, results) = Zones(frontend, host, session);
            _ = Barriers(frontend, host, session, results["zone_set"].GetUInt32(), (3, 0, 0, 0, 119));
            PortalBusTests.Await(host, SessionCall(frontend, session, "Enable"));

            var hyprProxy = HyprlandTestSupport.Bind<Basin.Hypr.InputCapture.Protocol.HyprlandInputCaptureManagerV1>(host, "hyprland_input_capture_manager_v1", 1);
            var hyprSession = hyprProxy.CreateSession("hypr");
            var hyprActivated = 0;
            var hyprFd = -1;
            hyprSession.Activated += (_, _) => hyprActivated++;
            hyprSession.EisFd += (_, e) => hyprFd = e.Fd;
            host.PumpUntil(() => hyprFd >= 0);
            new Microsoft.Win32.SafeHandles.SafeFileHandle(hyprFd, ownsHandle: true).Dispose();
            hyprSession.AddBarrier(1, 5, 160, 0, 160, 119);
            hyprSession.Enable();
            host.PumpToServer();
            Assert.Equal(2, engine.SessionCount);

            Assert.True(engine.NotifyMotion(1, -3, 50, -8, 0));
            Assert.True(engine.NotifyMotion(2, -5, 52, -2, 2));
            PumpEi();
            Assert.Equal([(-8.0, 0.0), (-2.0, 2.0)], motions);

            Assert.True(engine.NotifyMotion(3, 165, 50, 170, 0));
            host.PumpToClient();
            Assert.Equal(0, hyprActivated);
            Assert.True(engine.IsCaptured);

            ei.Disconnect();
            PumpEi();
            PortalBusTests.Await(host, frontend.CallAsync(session.ToString(), "org.freedesktop.impl.portal.Session", "Close"));
            Assert.False(engine.IsCaptured);
            Assert.False(engine.NotifyMotion(4, 100, 50, 1, 0));
            Assert.True(engine.NotifyMotion(5, 161, 50, 3, 0));
            host.PumpUntil(() => hyprActivated == 1);
        }
    }

    [Fact]
    public void A_capture_session_through_the_distro_frontend_activates_on_the_bus()
    {
        PortalFrontendFixture.SkipUnlessAvailable();
        Assert.SkipUnless(EisLibrary.IsAvailable(out var whyNot), whyNot ?? "libeis");
        using var host = new CompositorTestHost(160, 120);
        using var fixture = PortalFrontendFixture.Start(Impl);
        var (bus, services, engine) = Setup(host, fixture.Address, new AutoAnswerPrompts());
        using (bus)
        using (services)
        using (engine)
        {
            fixture.StartFrontend(host, bus);
            using var client = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(fixture.Address, PortalBus.FrontendName, asFrontend: false));
            var capture = new InputCapture(client.Connection, PortalBus.FrontendName, new ObjectPath(PortalBus.RootPath));
            Assert.Equal(2u, PortalBusTests.Await(host, capture.GetVersionAsync()));

            var sessionOptions = new Dictionary<string, VariantValue> { ["capabilities"] = VariantValue.UInt32(3) };
            var sessionPath = PortalClientRequests.SessionToken(client.Connection, sessionOptions);
            var created = PortalClientRequests.Run(host, client.Connection, o => capture.CreateSessionAsync("", o), sessionOptions);
            Assert.True(created.Response == 0, "response " + created.Response + "\n" + fixture.Dump());
            Assert.Equal(3u, created.Results["capabilities"].GetUInt32());

            var zones = PortalClientRequests.Run(host, client.Connection, o => capture.GetZonesAsync(sessionPath, o));
            Assert.Equal(0u, zones.Response);
            var zoneSet = zones.Results["zone_set"].GetUInt32();
            var barriers = PortalClientRequests.Run(host, client.Connection, o => capture.SetPointerBarriersAsync(sessionPath, o,
            [
                new Dictionary<string, VariantValue>
                {
                    ["barrier_id"] = VariantValue.UInt32(7),
                    ["position"] = VariantValue.Struct(VariantValue.Int32(0), VariantValue.Int32(0), VariantValue.Int32(0), VariantValue.Int32(119)),
                },
            ], zoneSet));
            Assert.Equal(0u, barriers.Response);
            Assert.Empty(barriers.Results["failed_barriers"].GetArray<uint>());

            using var eisHandle = PortalBusTests.Await(host, capture.ConnectToEISAsync(sessionPath, []));
            using var ei = EiContext.CreateReceiver("basin-test");
            var eisFd = (int)eisHandle.DangerousGetHandle();
            eisHandle.SetHandleAsInvalid();
            ei.ConnectToFd(eisFd);
            for (var round = 0; round < 10; round++)
            {
                host.Loop.Dispatch(0);
                ei.Dispatch();
                while (ei.TryGetEvent(out var @event))
                {
                    using (@event)
                    {
                        if (@event.Type == EiEventType.SeatAdded)
                        {
                            using var seat = @event.GetSeat();
                            seat!.BindCapabilities(EiDeviceCapability.Pointer | EiDeviceCapability.Button | EiDeviceCapability.Scroll | EiDeviceCapability.Keyboard);
                        }
                    }
                }
            }

            var activations = new List<uint>();
            using var watch = PortalBusTests.Await(host, capture.WatchActivatedAsync(e => activations.Add(e.Options["activation_id"].GetUInt32()), emitOnCapturedContext: false).AsTask());
            PortalBusTests.Await(host, capture.EnableAsync(sessionPath, []));
            PortalBusTests.PumpUntil(host, () => engine.SessionCount == 1);
            Assert.True(engine.NotifyMotion(1, -3, 50, -8, 0));
            PortalBusTests.PumpUntil(host, () => activations.Count == 1, rounds: 1500);
            Assert.Equal([1u], activations);

            PortalBusTests.Await(host, capture.ReleaseAsync(sessionPath, new Dictionary<string, VariantValue> { ["activation_id"] = VariantValue.UInt32(1) }));
            PortalBusTests.PumpUntil(host, () => !engine.IsCaptured);
            ei.Disconnect();
        }
    }
}
