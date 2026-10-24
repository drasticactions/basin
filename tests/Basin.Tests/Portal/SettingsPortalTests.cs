using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Portal;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class SettingsPortalTests
{
    private static ProtocolPack SettingsOnly => PortalPack.Default
        .Without("org.freedesktop.impl.portal.Screenshot")
        .Without("org.freedesktop.impl.portal.ScreenCast")
        .Without("org.freedesktop.impl.portal.RemoteDesktop")
        .Without("org.freedesktop.impl.portal.Clipboard")
        .Without("org.freedesktop.impl.portal.InputCapture")
        .Without("org.freedesktop.impl.portal.GlobalShortcuts")
        .Without("org.freedesktop.impl.portal.Access");

    private static Task<Dictionary<string, Dictionary<string, VariantValue>>> ReadAllAsync(PortalTestClient client, params string[] namespaces) =>
        client.CallRawAsync(
            "org.freedesktop.impl.portal.Settings",
            "ReadAll",
            "as",
            (ref MessageWriter writer) => writer.WriteArray(namespaces),
            static (Message m, object? _) =>
            {
                var reader = m.GetBodyReader();
                var result = new Dictionary<string, Dictionary<string, VariantValue>>();
                var outer = reader.ReadDictionaryStart();
                while (reader.HasNext(outer))
                {
                    var name = reader.ReadString();
                    result[name] = reader.ReadDictionaryOfStringToVariantValue();
                }

                return result;
            });

    private static Task<VariantValue> ReadAsync(PortalTestClient client, string key) =>
        client.CallRawAsync(
            "org.freedesktop.impl.portal.Settings",
            "Read",
            "ss",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(PortalSettingsModule.Appearance);
                writer.WriteString(key);
            },
            static (Message m, object? _) => m.GetBodyReader().ReadVariantValue());

    [Fact]
    public void The_appearance_settings_read_and_a_change_is_signalled_key_by_key()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        var appearance = new AppearanceSettingsStore();
        appearance.Set(new AppearancePreferences(ColorSchemePreference.Dark, new AccentColor(0.2, 0.4, 0.8), false, true));
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use<IAppearanceSettings>(appearance)
            .Install(SettingsOnly)
            .Freeze();
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var all = PortalBusTests.Await(host, ReadAllAsync(frontend, "org.freedesktop.*"));
        var values = all[PortalSettingsModule.Appearance];
        Assert.Equal(1u, values["color-scheme"].GetUInt32());
        Assert.Equal(0u, values["contrast"].GetUInt32());
        Assert.Equal(1u, values["reduced-motion"].GetUInt32());
        Assert.Equal(0.4, values["accent-color"].GetItem(1).GetDouble(), 6);
        Assert.Empty(PortalBusTests.Await(host, ReadAllAsync(frontend, "org.gnome.*")));
        Assert.Equal(1u, PortalBusTests.Await(host, ReadAsync(frontend, "color-scheme")).GetUInt32());
        var missing = Assert.Throws<DBusErrorReplyException>(() => PortalBusTests.Await(host, ReadAsync(frontend, "no-such-key")));
        Assert.Equal(PortalError.NotFound, missing.ErrorName);

        var changed = new List<(string Key, VariantValue Value)>();
        using var subscription = PortalBusTests.Await(host, frontend.Connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.impl.portal.Settings", Member = "SettingChanged" },
            static (Message m, object? _) =>
            {
                var reader = m.GetBodyReader();
                reader.ReadString();
                return (reader.ReadString(), reader.ReadVariantValue());
            },
            (Notification<(string, VariantValue)> n) =>
            {
                if (n.HasValue)
                {
                    lock (changed)
                    {
                        changed.Add(n.Value);
                    }
                }
            },
            false,
            ObserverFlags.None,
            null).AsTask());

        appearance.Set(new AppearancePreferences(ColorSchemePreference.Dark, new AccentColor(0.2, 0.4, 0.8), true, true));
        PortalBusTests.PumpUntil(host, () =>
        {
            lock (changed)
            {
                return changed.Count > 0;
            }
        });
        lock (changed)
        {
            var (key, value) = Assert.Single(changed);
            Assert.Equal("contrast", key);
            Assert.Equal(1u, value.GetUInt32());
        }
    }

    [Fact]
    public void Without_appearance_settings_the_interface_is_not_offered()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use<IPortalPrompts>(new AutoAnswerPrompts())
            .Install(PortalPack.Default.Without("org.freedesktop.impl.portal.ScreenCast").Without("org.freedesktop.impl.portal.RemoteDesktop").Without("org.freedesktop.impl.portal.InputCapture").Without("org.freedesktop.impl.portal.GlobalShortcuts").Without("org.freedesktop.impl.portal.Clipboard").Without("org.freedesktop.impl.portal.Screenshot"))
            .Freeze();
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));
        var root = PortalBusTests.Await(host, frontend.IntrospectAsync(PortalBus.RootPath));
        Assert.Contains("org.freedesktop.impl.portal.Access", root);
        Assert.DoesNotContain("org.freedesktop.impl.portal.Settings", root);
    }

    [Fact]
    public void An_application_reads_the_color_scheme_through_the_distro_frontend()
    {
        PortalFrontendFixture.SkipUnlessAvailable();
        using var host = new CompositorTestHost();
        using var fixture = PortalFrontendFixture.Start("org.freedesktop.impl.portal.Settings");
        using var bus = new PortalBus(host.Loop, fixture.Address, PortalBusTests.BusName);
        var appearance = new AppearanceSettingsStore();
        appearance.Set(new AppearancePreferences(ColorSchemePreference.Light, null, true, false));
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use<IAppearanceSettings>(appearance)
            .Install(SettingsOnly)
            .Freeze();
        PortalBusTests.Await(host, bus.Started);
        fixture.StartFrontend(host, bus);

        using var client = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(fixture.Address, PortalBus.FrontendName, asFrontend: false));
        VariantValue ReadOne(string key) => PortalBusTests.Await(host, client.CallRawAsync(
            "org.freedesktop.portal.Settings",
            "ReadOne",
            "ss",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(PortalSettingsModule.Appearance);
                writer.WriteString(key);
            },
            static (Message m, object? _) => m.GetBodyReader().ReadVariantValue()));

        Assert.True(ReadOne("color-scheme").GetUInt32() == 2u, fixture.Dump());
        Assert.Equal(1u, ReadOne("contrast").GetUInt32());
        Assert.Equal(0u, ReadOne("reduced-motion").GetUInt32());
    }

    [Fact]
    public void The_compositor_serves_settings_alone_on_its_own_name()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        var appearance = new AppearanceSettingsStore();
        appearance.Set(new AppearancePreferences(ColorSchemePreference.Light, null, true, true));
        using var service = PortalSettingsService.TryStart(host.Loop, appearance, daemon.Address, null, out var reason);
        Assert.True(service is not null, reason);
        PortalBusTests.Await(host, service.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalSettingsService.BusName, asFrontend: true));
        var values = PortalBusTests.Await(host, ReadAllAsync(frontend, "org.freedesktop.appearance"))[PortalSettingsModule.Appearance];
        Assert.Equal(2u, values["color-scheme"].GetUInt32());
        Assert.Equal(1u, values["contrast"].GetUInt32());
        Assert.Equal(1u, values["reduced-motion"].GetUInt32());
        var root = PortalBusTests.Await(host, frontend.IntrospectAsync(PortalBus.RootPath));
        Assert.Contains("org.freedesktop.impl.portal.Settings", root);
        Assert.DoesNotContain("org.freedesktop.impl.portal.Screenshot", root);
    }

    [Fact]
    public void Registration_without_settings_routes_settings_to_the_compositor()
    {
        var home = Directory.CreateTempSubdirectory("basin-portal-registration-").FullName;
        try
        {
            string[] interfaces = [.. PortalRegistration.AllInterfaces.Where(static name => name != "org.freedesktop.impl.portal.Settings")];
            var (_, configPath, _) = PortalRegistration.Write(interfaces, null, home);
            Assert.Contains("org.freedesktop.impl.portal.Settings=basin-settings;", File.ReadAllText(configPath), StringComparison.Ordinal);
            var settings = File.ReadAllText(Path.Combine(home, "xdg-desktop-portal", "portals", "basin-settings.portal"));
            Assert.Contains($"DBusName={PortalSettingsService.BusName}", settings, StringComparison.Ordinal);
            Assert.Contains("Interfaces=org.freedesktop.impl.portal.Settings;", settings, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
