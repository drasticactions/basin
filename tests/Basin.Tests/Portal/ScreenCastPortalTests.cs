using Basin.Backend.Headless;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Portal;
using Basin.Scene;
using Basin.Screencast.PipeWire;
using Basin.Tests.PortalClient;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class ScreenCastPortalTests
{
    private const string Impl = "org.freedesktop.impl.portal.ScreenCast";

    private static BasinServices Services(CompositorTestHost host, PortalBus bus, IScreenCapture capture, IScreencastPublisher publisher, IPortalPrompts prompts, Action<BasinServices>? more = null)
    {
        var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use(capture)
            .Use(publisher)
            .Use(prompts);
        more?.Invoke(services);
        return services.Install(PortalPack.Default.Without("org.freedesktop.impl.portal.Screenshot").Without("org.freedesktop.impl.portal.Clipboard").Without("org.freedesktop.impl.portal.InputCapture").Without("org.freedesktop.impl.portal.GlobalShortcuts")).Freeze();
    }

    private static ObjectPath NewSession(PortalTestClient frontend, CompositorTestHost host, string appId = "org.example.App", int n = 1)
    {
        var handle = new ObjectPath(PortalBus.RootPath + "/session/1_1/cast" + n);
        var (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "CreateSession", handle, appId, []));
        Assert.Equal(0u, response);
        Assert.Equal("cast" + n, results["session_id"].GetString());
        return handle;
    }

    [Fact]
    public void A_monitor_cast_goes_through_the_distro_frontend_and_reports_its_stream()
    {
        PortalFrontendFixture.SkipUnlessAvailable();
        using var host = new CompositorTestHost(64, 48);
        using var fixture = PortalFrontendFixture.Start(Impl);
        var capture = new TestScreenCapture(host);
        var publisher = new TestScreencastPublisher();
        var prompts = new AutoAnswerPrompts();
        using var bus = new PortalBus(host.Loop, fixture.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, capture, publisher, prompts);
        PortalBusTests.Await(host, bus.Started);
        fixture.StartFrontend(host, bus);

        using var client = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(fixture.Address, PortalBus.FrontendName, asFrontend: false));
        var cast = new ScreenCast(client.Connection, PortalBus.FrontendName, new ObjectPath(PortalBus.RootPath));
        Assert.Equal(6u, PortalBusTests.Await(host, cast.GetVersionAsync()));
        Assert.Equal(1u, PortalBusTests.Await(host, cast.GetAvailableSourceTypesAsync()));
        Assert.Equal(7u, PortalBusTests.Await(host, cast.GetAvailableCursorModesAsync()));

        var sessionOptions = new Dictionary<string, VariantValue>();
        var sessionPath = PortalClientRequests.SessionToken(client.Connection, sessionOptions);
        var created = PortalClientRequests.Run(host, client.Connection, o => cast.CreateSessionAsync(o), sessionOptions);
        Assert.Equal(0u, created.Response);
        Assert.Equal(sessionPath.ToString(), created.Results["session_handle"].GetString());
        Assert.Single(bus.Sessions);

        var selected = PortalClientRequests.Run(host, client.Connection, o => cast.SelectSourcesAsync(sessionPath, o), new Dictionary<string, VariantValue>
        {
            ["types"] = VariantValue.UInt32(1),
            ["cursor_mode"] = VariantValue.UInt32(PortalScreenCastModule.CursorEmbedded),
        });
        Assert.Equal(0u, selected.Response);

        var started = PortalClientRequests.Run(host, client.Connection, o => cast.StartAsync(sessionPath, "", o));
        Assert.True(started.Response == 0, "response " + started.Response + "\n" + fixture.Dump());
        var streams = started.Results["streams"];
        Assert.Equal(1, streams.Count);
        var stream = streams.GetItem(0);
        Assert.Equal(77u, stream.GetItem(0).GetUInt32());
        var properties = stream.GetItem(1).GetDictionary<string, VariantValue>();
        Assert.Equal((64, 48), (properties["size"].GetItem(0).GetInt32(), properties["size"].GetItem(1).GetInt32()));
        Assert.Equal(1u, properties["source_type"].GetUInt32());
        Assert.Equal(host.Output.Name, properties["mapping_id"].GetString());
        var request = Assert.Single(publisher.Requests);
        Assert.Equal(CaptureSourceKind.Output, request.Source.Kind);
        Assert.True(request.Source.OverlayCursor);
        Assert.Equal(ScreencastCursorMode.Embedded, request.Cursor);
        var prompt = Assert.IsType<SourcePrompt>(Assert.Single(prompts.Asked));
        Assert.Equal(PromptSourceKinds.Monitor, prompt.Kinds);
        Assert.Single(prompt.Outputs);

        var session = new PortalClient.Session(client.Connection, PortalBus.FrontendName, sessionPath);
        PortalBusTests.Await(host, session.CloseAsync());
        PortalBusTests.PumpUntil(host, () => bus.Sessions.Count == 0);
        Assert.Equal([request.StreamId], publisher.ClosedStreams);
    }

    [Fact]
    public void Restore_data_skips_the_prompt_when_the_output_is_still_there()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        var publisher = new TestScreencastPublisher();
        var prompts = new AutoAnswerPrompts { PersistMode = 2 };
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, new TestScreenCapture(host), publisher, prompts);
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var first = NewSession(frontend, host);
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectSources", first, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["persist_mode"] = VariantValue.UInt32(2),
        }));
        Assert.Equal(0u, response);
        Dictionary<string, VariantValue> results;
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", first, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Equal(2u, results["persist_mode"].GetUInt32());
        var restore = results["restore_data"];
        Assert.Equal(VariantValueType.Struct, restore.Type);
        Assert.Equal("basin", restore.GetItem(0).GetString());
        Assert.Single(prompts.Asked);

        var second = NewSession(frontend, host, n: 2);
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectSources", second, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["persist_mode"] = VariantValue.UInt32(2),
            ["restore_data"] = restore,
        }));
        Assert.Equal(0u, response);
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", second, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Single(prompts.Asked);
        Assert.Equal(2, publisher.Requests.Count);
        Assert.Equal(2u, results["persist_mode"].GetUInt32());

        var third = NewSession(frontend, host, n: 3);
        var inner = restore.GetItem(2);
        if (inner.Type == VariantValueType.Variant)
        {
            inner = inner.GetVariantValue();
        }

        var foreign = Results.Restore("KDE", 1, inner);
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectSources", third, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["restore_data"] = foreign,
        }));
        Assert.Equal(0u, response);
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", third, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Equal(2, prompts.Asked.Count);
    }

    [Fact]
    public void A_cursor_mode_that_is_not_offered_closes_the_session()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, new TestScreenCapture(host), new TestScreencastPublisher(), new AutoAnswerPrompts());
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var session = NewSession(frontend, host);
        var closed = 0;
        Action<Notification> onClosed = _ => closed++;
        using var watch = PortalBusTests.Await(host, frontend.Connection.WatchSignalAsync(
            PortalBusTests.BusName, session.ToString(), "org.freedesktop.impl.portal.Session", "Closed", onClosed, ObserverFlags.None, emitOnCapturedContext: false).AsTask());
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectSources", session, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["cursor_mode"] = VariantValue.UInt32(8),
        }));
        Assert.Equal(2u, response);
        Assert.Empty(bus.Sessions);
        PortalBusTests.PumpUntil(host, () => closed == 1);
    }

    [Fact]
    public void A_window_cast_and_a_destroyed_output_end_where_they_should()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        var model = new TestToplevelModel();
        var window = model.Add("Editor", "org.example.Editor", geometry: new Box(5, 5, 30, 20));
        var publisher = new TestScreencastPublisher();
        var prompts = new AutoAnswerPrompts { Sources = AutoAnswerPrompts.SourceAnswer.FirstToplevel };
        var second = host.Backend.CreateOutput(new OutputMode(32, 24, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 64, 0);
        var outputs = new LayoutOutputSet(host.Layout);
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, new TestScreenCapture(host), publisher, prompts, s => s.Use<IToplevelModel>(model).Use<IOutputSet>(outputs));
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));
        Assert.Equal(3u, PortalBusTests.Await(host, frontend.GetPropertyAsync(PortalBus.RootPath, Impl, "AvailableSourceTypes")).GetUInt32());

        var windowSession = NewSession(frontend, host);
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectSources", windowSession, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["types"] = VariantValue.UInt32(2),
        }));
        Assert.Equal(0u, response);
        Dictionary<string, VariantValue> results;
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", windowSession, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        var stream = results["streams"].GetItem(0).GetItem(1).GetDictionary<string, VariantValue>();
        Assert.Equal(2u, stream["source_type"].GetUInt32());
        Assert.Equal($"toplevel-{window}", stream["mapping_id"].GetString());
        Assert.Equal((30, 20), (stream["size"].GetItem(0).GetInt32(), stream["size"].GetItem(1).GetInt32()));
        Assert.False(stream.ContainsKey("position"));
        Assert.Equal(CaptureSourceKind.Toplevel, publisher.Requests[^1].Source.Kind);

        prompts.Sources = AutoAnswerPrompts.SourceAnswer.AllOutputs;
        var monitorSession = NewSession(frontend, host, n: 2);
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectSources", monitorSession, "org.example.App", new Dictionary<string, VariantValue>
        {
            ["multiple"] = VariantValue.Bool(true),
        }));
        Assert.Equal(0u, response);
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", monitorSession, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        Assert.Equal(2, results["streams"].Count);
        var far = results["streams"].GetItem(1).GetItem(1).GetDictionary<string, VariantValue>();
        Assert.Equal((64, 0), (far["position"].GetItem(0).GetInt32(), far["position"].GetItem(1).GetInt32()));
        Assert.Equal(2, bus.Sessions.Count);

        second.Destroy();
        host.Loop.Dispatch(0);
        Assert.Single(bus.Sessions);
        Assert.Contains(windowSession.ToString(), bus.Sessions.Keys);
        Assert.Equal(2, publisher.ClosedStreams.Count);
    }

    [Fact]
    public void A_real_stream_flows_through_pipewire()
    {
        Assert.SkipUnless(PipeWireLibraryProbe.IsAvailable(out var whyNot), whyNot ?? "libpipewire");
        Assert.SkipUnless(PipeWireLibraryProbe.IsDaemonReachable(), "no PipeWire daemon");
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(0f, 1f, 0f, 1f));
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        using var publisher = PipeWireScreencastPublisher.TryCreate(host.Loop, capture, host.Layout, clientName: "basin-test");
        Assert.NotNull(publisher);
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, capture, publisher, new AutoAnswerPrompts());
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var session = NewSession(frontend, host);
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "SelectSources", session, "org.example.App", []));
        Assert.Equal(0u, response);
        Dictionary<string, VariantValue> results;
        (response, results) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "Start", session, "org.example.App", [], parentWindow: ""));
        Assert.Equal(0u, response);
        var stream = results["streams"].GetItem(0);
        var nodeId = stream.GetItem(0).GetUInt32();
        Assert.NotEqual(0u, nodeId);
        var properties = stream.GetItem(1).GetDictionary<string, VariantValue>();
        Assert.True(properties["pipewire-serial"].GetUInt64() != 0);
        Assert.Equal(1, publisher.StreamCount);

        PortalBusTests.Await(host, frontend.CallAsync(session.ToString(), "org.freedesktop.impl.portal.Session", "Close"));
        Assert.Equal(0, publisher.StreamCount);
    }

    [Fact]
    public void Without_a_publisher_the_module_refuses_to_freeze()
    {
        using var host = new CompositorTestHost();
        using var bus = new PortalBus(host.Loop, "unix:path=/nonexistent", PortalBusTests.BusName);
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use<IScreenCapture>(new TestScreenCapture(host))
            .Use<IPortalPrompts>(new AutoAnswerPrompts())
            .Install(PortalPack.Default.Without("org.freedesktop.impl.portal.Screenshot").Without("org.freedesktop.impl.portal.Clipboard").Without("org.freedesktop.impl.portal.InputCapture").Without("org.freedesktop.impl.portal.GlobalShortcuts"));
        var error = Assert.Throws<InvalidOperationException>(() => services.Freeze());
        Assert.Contains(Impl, error.Message);
        Assert.Contains(nameof(IScreencastPublisher), error.Message);
    }
}
