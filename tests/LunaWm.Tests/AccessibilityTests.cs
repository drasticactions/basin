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

    [Fact]
    public async Task The_launcher_takes_the_keyboard_and_tab_and_enter_reach_its_buttons()
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
        var launcher = await reader.FindAsync("Launcher", "frame");
        await session.SendAsync("launcher");
        Assert.NotNull(await session.WaitForAsync(line => line == "LAUNCHER on"));
        await reader.WaitForStateAsync(launcher, AtSpiState.Active);

        var downloads = await reader.FindAsync("Downloads", "push button");
        for (var i = 0; i < 12 && !await reader.HasStateAsync(downloads, AtSpiState.Focused); i++)
        {
            await session.SendAsync("key 15 1");
            await session.SendAsync("key 15 0");
            await Task.Delay(150, TestContext.Current.CancellationToken);
        }

        await reader.WaitForStateAsync(downloads, AtSpiState.Focused);
        await session.SendAsync("key 28 1");
        await session.SendAsync("key 28 0");
        for (var i = 0; i < 50; i++)
        {
            var mark = session.Mark();
            await session.SendAsync("where");
            if (await session.WaitForAsync(line => line.StartsWith("  launcher ", StringComparison.Ordinal), since: mark) is { } state
                && state.Contains("tab=Downloads", StringComparison.Ordinal))
            {
                break;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Contains(session.Lines(), line => line.StartsWith("  launcher open tab=Downloads", StringComparison.Ordinal));
        await session.SendAsync("launcher close");
        Assert.NotNull(await session.WaitForAsync(line => line == "LAUNCHER off"));
        await reader.WaitForStateAsync(launcher, AtSpiState.Active, value: false);
        await session.SendAsync("quit");
    }
}
