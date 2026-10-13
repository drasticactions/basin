using Xunit;

namespace BasinGreeter.Tests;

public sealed class StatusTests
{
    [Fact]
    public async Task Battery_and_network_come_from_upower_and_networkmanager()
    {
        using var system = await FakeSystem.StartAsync();
        using var fixture = new Fixture();
        var environment = fixture.Environment;
        environment["DBUS_SYSTEM_BUS_ADDRESS"] = system.Bus.Address;
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
        using var greeter = new GreeterSession(GreeterSession.Headless("pixman", fixture.ConfigPath, "--greetd-sock", fixture.Greetd.SocketPath, "--state", fixture.StatePath), environment);
        await greeter.ExpectAsync(line => line == "STATUS battery=43% network=wireless:Home", "battery and wireless status");
    }

    [Fact]
    public async Task Without_a_system_bus_the_status_hides_and_the_config_can_turn_it_off()
    {
        using (var fixture = new Fixture())
        using (var greeter = fixture.Start())
        {
            await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
            await greeter.ExpectAsync("STATUS battery=none network=none");
        }

        using var system = await FakeSystem.StartAsync();
        using var off = new Fixture("[status]\nbattery = false\n\n[power]\nenabled = false\n");
        var environment = off.Environment;
        environment["DBUS_SYSTEM_BUS_ADDRESS"] = system.Bus.Address;
        Directory.CreateDirectory(Path.GetDirectoryName(off.StatePath)!);
        using var session = new GreeterSession(GreeterSession.Headless("pixman", off.ConfigPath, "--greetd-sock", off.Greetd.SocketPath, "--state", off.StatePath), environment);
        await session.ExpectAsync(line => line == "STATUS battery=none network=wireless:Home", "network only");
        Assert.DoesNotContain(session.Lines(), line => line.StartsWith("POWER can", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restart_and_shut_down_ask_first_and_no_call_reaches_logind_in_tests()
    {
        using var fixture = new Fixture();
        using var greeter = fixture.Start();
        await greeter.ExpectAsync("POWER can suspend=yes restart=yes shutdown=yes");
        await greeter.SendAsync("power-ask restart");
        await greeter.ExpectAsync("CONFIRM restart");
        await greeter.SendAsync("power-ask cancel");
        await greeter.ExpectAsync("CONFIRM none");
        await greeter.SendAsync("power-ask shutdown");
        await greeter.ExpectAsync("CONFIRM shutdown");
        await greeter.SendAsync("power-ask confirm");
        await greeter.ExpectAsync("POWER shutdown skipped");
        await greeter.SendAsync("power suspend");
        await greeter.ExpectAsync("POWER suspend skipped");
    }

    [Fact]
    public async Task A_confirmed_restart_calls_logind_reboot_without_interaction()
    {
        Assert.SkipUnless(Directory.Exists("/run/systemd/system"), "LogindActions needs a systemd host");
        using var bus = Basin.Tests.PrivateBus.Start();
        using var logind = await Basin.Tests.MockSystemService.StartAsync(bus.Address, "org.freedesktop.login1");
        logind.OnMethod = context =>
        {
            var member = context.Request.MemberAsString ?? "";
            if (member.StartsWith("Can", StringComparison.Ordinal))
            {
                using var writer = context.CreateReplyWriter("s");
                writer.WriteString(member == "CanSuspend" ? "na" : "challenge");
                context.Reply(writer.CreateMessage());
                return true;
            }

            return false;
        };
        using var fixture = new Fixture();
        var environment = fixture.Environment;
        environment["DBUS_SYSTEM_BUS_ADDRESS"] = bus.Address;
        environment["BASIN_GREETER_NO_POWER"] = null;
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
        using var greeter = new GreeterSession(GreeterSession.Headless("pixman", fixture.ConfigPath, "--greetd-sock", fixture.Greetd.SocketPath, "--state", fixture.StatePath), environment);
        await greeter.ExpectAsync("POWER can suspend=no restart=yes shutdown=yes");
        await greeter.SendAsync("power-ask restart");
        await greeter.ExpectAsync("CONFIRM restart");
        await greeter.SendAsync("power-ask confirm");
        await greeter.ExpectAsync("POWER restart requested");
        for (var i = 0; i < 50 && !logind.Calls().Any(call => call.Member == "Reboot"); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        var reboot = Assert.Single(logind.Calls(), call => call.Member == "Reboot");
        Assert.False(reboot.Arguments[0].GetBool());
    }
}
