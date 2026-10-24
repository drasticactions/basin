using Basin.Testing;
using Xunit;

namespace LunaWm.Tests;

public sealed class AccessibilityTests
{
    [Fact]
    public async Task The_status_bar_is_on_the_bus_and_a_reader_opens_the_device_menu_from_it()
    {
        using var bus = await AccessibilityBus.StartAsync();
        using var system = PrivateBus.Start();
        using var config = new TempConfig(TempConfig.Quiet + "\n[accessibility]\nenabled = \"on\"\n");
        var environment = config.Environment;
        foreach (var (name, value) in bus.Apply(new Dictionary<string, string?>()))
        {
            environment[name] = value!;
        }

        environment["DBUS_SYSTEM_BUS_ADDRESS"] = system.Address;
        environment["LUNAWM_NO_POWER"] = "1";
        using var session = new LunaSession(LunaSession.Compositor(), config.Path, environment);
        Assert.NotNull(await session.WaitForAsync(line => line == "A11Y on"));
        using var reader = await bus.ConnectAsync();
        await reader.FindAsync("Status bar", "frame");
        var battery = await reader.FindAsync("Battery", "push button");
        Assert.True(await reader.DoActionAsync(battery, "click"));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MENU ", StringComparison.Ordinal) && line.EndsWith(" on", StringComparison.Ordinal)));
        await reader.FindAsync("Menu", "frame");
        Assert.DoesNotContain(await reader.TreeAsync(), node => node.Name.Contains("LunaWm.", StringComparison.Ordinal));
        await session.SendAsync("quit");
    }
}
