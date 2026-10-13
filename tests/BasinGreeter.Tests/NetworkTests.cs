using Tmds.DBus.Protocol;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class NetworkTests
{
    private static async Task<GreeterSession> Start(FakeSystem system, Fixture fixture)
    {
        var environment = fixture.Environment;
        environment["DBUS_SYSTEM_BUS_ADDRESS"] = system.Bus.Address;
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
        var greeter = new GreeterSession(GreeterSession.Headless("pixman", fixture.ConfigPath, "--greetd-sock", fixture.Greetd.SocketPath, "--state", fixture.StatePath), environment);
        await greeter.ExpectAsync(line => line == "STATUS battery=43% network=wireless:Home", "the network status");
        return greeter;
    }

    private static Dictionary<string, Dictionary<string, VariantValue>> Settings(Message request)
    {
        var reader = request.GetBodyReader();
        var settings = new Dictionary<string, Dictionary<string, VariantValue>>();
        var groups = reader.ReadDictionaryStart();
        while (reader.HasNext(groups))
        {
            var name = reader.ReadString();
            settings[name] = reader.ReadDictionaryOfStringToVariantValue();
        }

        return settings;
    }

    [Fact]
    public async Task The_menu_joins_saved_open_psk_and_sae_networks_and_refuses_enterprise()
    {
        using var system = await FakeSystem.StartAsync();
        var sent = new List<Dictionary<string, Dictionary<string, VariantValue>>>();
        system.AddAndActivate = context =>
        {
            lock (sent)
            {
                sent.Add(Settings(context.Request));
            }

            return false;
        };
        using var fixture = new Fixture();
        using var greeter = await Start(system, fixture);
        await greeter.SendAsync("network open");
        await greeter.ExpectAsync("NETWORKMENU open points=5");

        await greeter.SendAsync("network-join Cafe");
        await greeter.ExpectAsync("NETWORK join Cafe open");
        await greeter.ExpectAsync("NETWORK Cafe ok");

        await greeter.SendAsync("network-join Office");
        await greeter.ExpectAsync("NETWORK ask Office psk");
        await greeter.SendAsync("network-passphrase correct horse");
        await greeter.ExpectAsync("NETWORK join Office secured");
        await greeter.ExpectAsync("NETWORK Office ok");

        await greeter.SendAsync("network-join Lab");
        await greeter.ExpectAsync("NETWORK ask Lab sae");
        await greeter.SendAsync("network-passphrase battery staple");
        await greeter.ExpectAsync("NETWORK Lab ok");

        await greeter.SendAsync("network-join Corp");
        await greeter.ExpectAsync("NETWORK join Corp unsupported");

        Assert.DoesNotContain(greeter.Lines().Concat(greeter.Errors), line => line.Contains("correct horse", StringComparison.Ordinal) || line.Contains("battery staple", StringComparison.Ordinal));
        lock (sent)
        {
            Assert.Equal(3, sent.Count);
            Assert.Empty(sent[0]);
            Assert.Equal("wpa-psk", sent[1]["802-11-wireless-security"]["key-mgmt"].GetString());
            Assert.Equal("correct horse", sent[1]["802-11-wireless-security"]["psk"].GetString());
            Assert.Equal("sae", sent[2]["802-11-wireless-security"]["key-mgmt"].GetString());
            Assert.All(sent, settings => Assert.False(settings.ContainsKey("connection")));
        }

        await greeter.SendAsync("wifi off");
        await greeter.ExpectAsync("WIFI off");
        for (var i = 0; i < 50 && !system.Network.Calls().Any(call => call.Member == "Set:WirelessEnabled"); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.False(system.Network.Calls().Single(call => call.Member == "Set:WirelessEnabled").Arguments[0].GetBool());
    }

    [Fact]
    public async Task A_refusal_shows_networkmanagers_words_and_join_false_keeps_the_menu_shut()
    {
        using var system = await FakeSystem.StartAsync();
        system.AddAndActivate = context =>
        {
            context.ReplyError("org.freedesktop.NetworkManager.PermissionDenied", "Insufficient privileges");
            return true;
        };
        using (var fixture = new Fixture())
        using (var greeter = await Start(system, fixture))
        {
            await greeter.SendAsync("network open");
            await greeter.SendAsync("network-join Office");
            await greeter.ExpectAsync("NETWORK ask Office psk");
            await greeter.SendAsync("network-passphrase pw");
            await greeter.ExpectAsync(line => line.StartsWith("NETWORK Office failed ", StringComparison.Ordinal) && line.Contains("Insufficient privileges", StringComparison.Ordinal), "the refusal text");
        }

        using var closed = new Fixture("[network]\njoin = false\n");
        using var readOnly = await Start(system, closed);
        await readOnly.SendAsync("network open");
        await readOnly.ExpectAsync("NETWORKMENU closed points=5");
    }
}
