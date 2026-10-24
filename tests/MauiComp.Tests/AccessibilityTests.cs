using Xunit;

namespace MauiComp.Tests;

public sealed class AccessibilityTests
{
    [Fact]
    public async Task The_taskbar_is_on_the_bus_and_a_reader_opens_the_start_menu()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Session.Locate("maui-comp");
        Assert.SkipWhen(compositor is null, "maui-comp has not been built beside the tests");
        using var bus = await Basin.Testing.AccessibilityBus.StartAsync();
        var directory = Directory.CreateTempSubdirectory("maui-a11y-");
        var config = Path.Combine(directory.FullName, "maui-comp.toml");
        File.WriteAllText(config, "[accessibility]\nenabled = \"on\"\n");
        try
        {
            var environment = bus.Apply(new Dictionary<string, string?>()).ToDictionary(pair => pair.Key, pair => pair.Value!);
            using var session = new Session(compositor!, environment, "pixman", "--config", config);
            Assert.NotNull(await session.WaitForAsync(line => line == "A11Y on"));
            using var reader = await bus.ConnectAsync();
            await reader.FindAsync("Taskbar", "frame");
            var start = await reader.FindAsync("Start", "push button");
            Assert.True(await reader.DoActionAsync(start, "click"));
            await reader.FindAsync("Start menu", "frame");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
