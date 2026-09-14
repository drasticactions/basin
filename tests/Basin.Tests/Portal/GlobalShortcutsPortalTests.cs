using Basin.Capabilities;
using Basin.Portal;
using Basin.Tests.PortalClient;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class GlobalShortcutsPortalTests
{
    private const string Impl = "org.freedesktop.impl.portal.GlobalShortcuts";

    private static BasinServices Services(CompositorTestHost host, PortalBus bus, IGlobalShortcuts registry, IPortalPrompts prompts) =>
        new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use(registry)
            .Use(prompts)
            .Install(new ProtocolPack([new PortalGlobalShortcutsModule()]))
            .Freeze();

    private static (string, Dictionary<string, VariantValue>) Shortcut(string id, string description, string? preferred = null)
    {
        var properties = new Dictionary<string, VariantValue> { ["description"] = VariantValue.String(description) };
        if (preferred is not null)
        {
            properties["preferred_trigger"] = VariantValue.String(preferred);
        }

        return (id, properties);
    }

    private static Task<(uint Response, Dictionary<string, VariantValue> Results)> BindAsync(PortalTestClient frontend, ObjectPath session, params (string, Dictionary<string, VariantValue>)[] shortcuts) =>
        frontend.CallRawAsync(Impl, "BindShortcuts", "ooa(sa{sv})sa{sv}", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(frontend.NextRequest());
            w.WriteObjectPath(session);
            var array = w.WriteArrayStart(DBusType.Struct);
            foreach (var (id, properties) in shortcuts)
            {
                w.WriteStructureStart();
                w.WriteString(id);
                w.WriteDictionary(properties);
            }

            w.WriteArrayEnd(array);
            w.WriteString("");
            w.WriteDictionary(new Dictionary<string, VariantValue>());
        }, static (Message m, object? _) => PortalTestClient.ReadResponse(m));

    private static Dictionary<string, (string Description, string Trigger)> Rows(VariantValue shortcuts)
    {
        var rows = new Dictionary<string, (string, string)>();
        for (var i = 0; i < shortcuts.Count; i++)
        {
            var item = shortcuts.GetItem(i);
            var properties = item.GetItem(1).GetDictionary<string, VariantValue>();
            rows[item.GetItem(0).GetString()] = (properties["description"].GetString(), properties["trigger_description"].GetString());
        }

        return rows;
    }

    [Fact]
    public void Preferred_triggers_bind_without_a_prompt_and_collisions_go_to_it()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        var registry = new TestShortcutRegistry();
        registry.Taken.Add("CTRL+SHIFT+r");
        var prompts = new AutoAnswerPrompts { ShortcutTrigger = "LOGO+F9" };
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, registry, prompts);
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var session = new ObjectPath(PortalBus.RootPath + "/session/1_1/gs1");
        var (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "CreateSession", session, "org.example.App", []));
        Assert.Equal(0u, response);

        Dictionary<string, VariantValue> results;
        (response, results) = PortalBusTests.Await(host, BindAsync(frontend, session, Shortcut("record", "Start recording", "CTRL+ALT+r"), Shortcut("nothing", "No trigger asked")));
        Assert.Equal(0u, response);
        var rows = Rows(results["shortcuts"]);
        Assert.Equal(("Start recording", "CTRL+ALT+r"), rows["record"]);
        Assert.Equal(("No trigger asked", "LOGO+F9"), rows["nothing"]);
        var prompt = Assert.IsType<ShortcutPrompt>(Assert.Single(prompts.Asked));
        Assert.Single(prompt.Shortcuts);
        Assert.Equal("nothing", prompt.Shortcuts[0].Id);

        var second = new ObjectPath(PortalBus.RootPath + "/session/1_1/gs2");
        (response, _) = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "CreateSession", second, "org.example.Other", []));
        prompts.ShortcutTrigger = "ALT+F12";
        (response, results) = PortalBusTests.Await(host, BindAsync(frontend, second, Shortcut("record", "Record", "CTRL+SHIFT+r")));
        Assert.Equal(0u, response);
        Assert.Equal(("Record", "ALT+F12"), Rows(results["shortcuts"])["record"]);
        var collision = Assert.IsType<ShortcutPrompt>(prompts.Asked[1]);
        Assert.True(collision.Shortcuts[0].PreferredTaken);

        (response, results) = PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "ListShortcuts", "oo", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(frontend.NextRequest());
            w.WriteObjectPath(session);
        }, static (Message m, object? _) => PortalTestClient.ReadResponse(m)));
        Assert.Equal(0u, response);
        Assert.Equal(2, Rows(results["shortcuts"]).Count);
        Assert.Equal(3, registry.Count);

        PortalBusTests.Await(host, frontend.CallAsync(session.ToString(), "org.freedesktop.impl.portal.Session", "Close"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void A_trigger_fires_activated_and_deactivated_on_the_owning_session_only()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        var registry = new TestShortcutRegistry();
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, registry, new AutoAnswerPrompts());
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var session = new ObjectPath(PortalBus.RootPath + "/session/1_1/gs1");
        var other = new ObjectPath(PortalBus.RootPath + "/session/1_1/gs2");
        _ = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "CreateSession", session, "org.example.App", []));
        _ = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "CreateSession", other, "org.example.Other", []));
        _ = PortalBusTests.Await(host, BindAsync(frontend, session, Shortcut("record", "Record", "CTRL+ALT+r")));
        _ = PortalBusTests.Await(host, BindAsync(frontend, other, Shortcut("record", "Record", "CTRL+ALT+o")));

        var events = new List<(string Path, string Member, string Id, ulong Timestamp)>();
        using var activated = PortalBusTests.Await(host, frontend.Connection.WatchSignalAsync<(ObjectPath, string, ulong)>(
            PortalBusTests.BusName, PortalBus.RootPath, Impl, "Activated",
            static (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPath(), r.ReadString(), r.ReadUInt64()); },
            (Notification<(ObjectPath, string, ulong)> n) => { if (n.HasValue) { events.Add((n.Value.Item1.ToString(), "Activated", n.Value.Item2, n.Value.Item3)); } },
            ObserverFlags.None, emitOnCapturedContext: false).AsTask());
        using var deactivated = PortalBusTests.Await(host, frontend.Connection.WatchSignalAsync<(ObjectPath, string, ulong)>(
            PortalBusTests.BusName, PortalBus.RootPath, Impl, "Deactivated",
            static (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPath(), r.ReadString(), r.ReadUInt64()); },
            (Notification<(ObjectPath, string, ulong)> n) => { if (n.HasValue) { events.Add((n.Value.Item1.ToString(), "Deactivated", n.Value.Item2, n.Value.Item3)); } },
            ObserverFlags.None, emitOnCapturedContext: false).AsTask());

        Assert.True(registry.Trigger("org.example.App", "record", pressed: true, 1234));
        Assert.True(registry.Trigger("org.example.App", "record", pressed: false, 1300));
        PortalBusTests.PumpUntil(host, () => events.Count == 2);
        Assert.Equal([(session.ToString(), "Activated", "record", 1234UL), (session.ToString(), "Deactivated", "record", 1300UL)], events);
    }

    [Fact]
    public void Configure_reprompts_every_shortcut_and_announces_the_change()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        var registry = new TestShortcutRegistry();
        var prompts = new AutoAnswerPrompts();
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, registry, prompts);
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var session = new ObjectPath(PortalBus.RootPath + "/session/1_1/gs1");
        _ = PortalBusTests.Await(host, frontend.CallSessionImplAsync(Impl, "CreateSession", session, "org.example.App", []));
        _ = PortalBusTests.Await(host, BindAsync(frontend, session, Shortcut("record", "Record", "CTRL+ALT+r"), Shortcut("stop", "Stop", "CTRL+ALT+s")));
        Assert.Empty(prompts.Asked);

        var changed = 0;
        Action<Notification> onChanged = _ => changed++;
        using var watch = PortalBusTests.Await(host, frontend.Connection.WatchSignalAsync(
            PortalBusTests.BusName, PortalBus.RootPath, Impl, "ShortcutsChanged", onChanged, ObserverFlags.None, emitOnCapturedContext: false).AsTask());

        prompts.ShortcutTrigger = "LOGO+r";
        PortalBusTests.Await(host, frontend.CallRawAsync(Impl, "ConfigureShortcuts", "osa{sv}", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteString("");
            w.WriteDictionary(new Dictionary<string, VariantValue>());
        }));
        var prompt = Assert.IsType<ShortcutPrompt>(Assert.Single(prompts.Asked));
        Assert.Equal(2, prompt.Shortcuts.Count);
        Assert.Equal("LOGO+r", registry["org.example.App", "record"].TriggerDescription);
        Assert.Equal("LOGO+r", registry["org.example.App", "stop"].TriggerDescription);
        PortalBusTests.PumpUntil(host, () => changed == 1);
    }

    [Fact]
    public void A_bind_through_the_distro_frontend_reports_the_bound_trigger_and_fires()
    {
        PortalFrontendFixture.SkipUnlessAvailable();
        using var host = new CompositorTestHost();
        using var fixture = PortalFrontendFixture.Start(Impl);
        var registry = new TestShortcutRegistry();
        using var bus = new PortalBus(host.Loop, fixture.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, registry, new AutoAnswerPrompts());
        PortalBusTests.Await(host, bus.Started);
        fixture.StartFrontend(host, bus);

        using var client = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(fixture.Address, PortalBus.FrontendName, asFrontend: false));
        var shortcuts = new GlobalShortcuts(client.Connection, PortalBus.FrontendName, new ObjectPath(PortalBus.RootPath));
        Assert.Equal(2u, PortalBusTests.Await(host, shortcuts.GetVersionAsync()));
        var sessionOptions = new Dictionary<string, VariantValue>();
        var sessionPath = PortalClientRequests.SessionToken(client.Connection, sessionOptions);
        try
        {
            Assert.Equal(0u, PortalClientRequests.Run(host, client.Connection, o => shortcuts.CreateSessionAsync(o), sessionOptions).Response);
        }
        catch (DBusErrorReplyException e) when (e.ErrorMessage.Contains("app id", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("the frontend refuses global shortcuts for a host process it cannot name: " + e.ErrorMessage);
        }

        var fired = new List<(string Id, bool Activated)>();
        using var watch = PortalBusTests.Await(host, shortcuts.WatchActivatedAsync(e => fired.Add((e.ShortcutId, true)), emitOnCapturedContext: false).AsTask());
        using var watchUp = PortalBusTests.Await(host, shortcuts.WatchDeactivatedAsync(e => fired.Add((e.ShortcutId, false)), emitOnCapturedContext: false).AsTask());
        var bound = PortalClientRequests.Run(host, client.Connection, o => shortcuts.BindShortcutsAsync(sessionPath, [Shortcut("record", "Start recording", "CTRL+SHIFT+r")], "", o));
        Assert.True(bound.Response == 0, "response " + bound.Response + "\n" + fixture.Dump());
        Assert.Equal(("Start recording", "CTRL+SHIFT+r"), Rows(bound.Results["shortcuts"])["record"]);
        var appId = registry.Count == 1 ? "" : "?";
        Assert.Equal(1, registry.Count);

        Assert.True(registry.Trigger(appId, "record", pressed: true, 5));
        Assert.True(registry.Trigger(appId, "record", pressed: false, 6));
        PortalBusTests.PumpUntil(host, () => fired.Count == 2, rounds: 1500);
        Assert.Equal([("record", true), ("record", false)], fired);
    }
}
