using Basin.Testing;
using Xunit;

namespace System7Wm.Tests;

public sealed class AccessibilityTests
{
    [Fact]
    public async Task The_menu_bar_and_desktop_are_on_the_bus_with_every_menu_named()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var directory = Directory.CreateTempSubdirectory("system7-a11y-");
        var config = Path.Combine(directory.FullName, "system7-wm.toml");
        File.WriteAllText(config, "[accessibility]\nenabled = \"on\"\n");
        try
        {
            var environment = bus.Apply(new Dictionary<string, string?>()).ToDictionary(pair => pair.Key, pair => pair.Value!);
            using var session = new Session(Prerequisites.Compositor(), "pixman", environment, "--config", config);
            Assert.NotNull(await session.WaitForLineAsync("A11Y on"));
            using var reader = await bus.ConnectAsync();
            var tree = await reader.WhereAsync(nodes => nodes.Any(n => n.Name == "Menu bar") && nodes.Any(n => n.Name == "Desktop"));
            Assert.Contains(tree, n => n.Role == "menu item" && n.Name == "Apple");
            Assert.Contains(tree, n => n.Role == "menu item" && n.Name == "File");
            Assert.DoesNotContain(tree, node => node.Name.Contains("System7Wm.", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
