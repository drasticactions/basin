using Basin.Testing;
using Xunit;

namespace EightWm.Tests;

public sealed class AccessibilityTests
{
    [Fact]
    public async Task The_charms_are_on_the_bus_and_a_reader_opens_settings_from_them()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = EightSession.Locate("eight-wm");
        Assert.SkipWhen(compositor is null, "eight-wm has not been built beside the tests");
        using var bus = await AccessibilityBus.StartAsync();
        var config = Path.Combine(Directory.CreateTempSubdirectory("eight-a11y-").FullName, "eight-wm.toml");
        File.WriteAllText(config, "[start]\nscan_desktop_files = false\n[accessibility]\nenabled = \"on\"\n");
        var environment = bus.Apply(new Dictionary<string, string?>()).ToDictionary(pair => pair.Key, pair => pair.Value!);
        using var session = new EightSession(compositor!, config, environment);
        Assert.NotNull(await session.WaitForAsync(line => line == "A11Y on"));
        await session.SendAsync("charms on");
        Assert.NotNull(await session.WaitForAsync(line => line == "CHARMS on"));
        using var reader = await bus.ConnectAsync();
        await reader.FindAsync("Charms", "frame");
        var settings = await reader.FindAsync("Settings", "push button");
        Assert.True(await reader.DoActionAsync(settings, "click"));
        Assert.NotNull(await session.WaitForAsync(line => line == "CHARM Settings"));
        await reader.FindAsync("Lock", "push button");
        Assert.DoesNotContain(await reader.TreeAsync(), node => node.Name.Contains("EightWm.", StringComparison.Ordinal));
        Directory.Delete(Path.GetDirectoryName(config)!, recursive: true);
    }
}
