using Tmds.DBus.Protocol;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class AtSpiTests
{
    [Fact]
    public async Task The_login_box_is_on_the_accessibility_bus_with_its_users_button_and_password_field()
    {
        using var bus = await AccessibilityBus.StartAsync();
        using var fixture = new Fixture();
        fixture.Greetd.Respond(new Basin.Greetd.GreetdAuthMessage(Basin.Greetd.GreetdAuthMessageType.Secret, "Password: "));
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
        using var greeter = new GreeterSession(GreeterSession.Headless("pixman", fixture.ConfigPath, "--greetd-sock", fixture.Greetd.SocketPath, "--state", fixture.StatePath), bus.Apply(fixture.Environment));
        await greeter.ExpectAsync("A11Y on");
        using var a11y = await bus.ConnectAsync();

        var list = await a11y.WhereAsync(nodes => nodes.Any(node => node.Name == "Log in"));
        Assert.True(list.Any(node => node.Role == "list item" && node.Name == "Ada Lovelace"), string.Join(" | ", list.Select(node => $"{node.Role}:{node.Name}")));
        Assert.Contains(list, node => node.Role == "push button" && node.Name == "Log in");
        Assert.Contains(list, node => node.Role == "push button" && node.Name == "Power");
        Assert.Contains(list, node => node.Role == "combo box" && node.Name == "Session");
        var users = list.First(node => node.Role == "list box" && node.Name == "Users");
        var items = await a11y.ChildrenAsync(users);
        Assert.True(items.Count > 1 && items.All(node => node.Role == "list item"), string.Join(" | ", items.Select(node => $"{node.Role}:{node.Name}")));

        await greeter.SendAsync("select ada");
        await greeter.ExpectAsync("AUTH secret Password: ");
        await greeter.SettleAsync();
        var prompt = await a11y.WhereAsync(nodes => nodes.Any(node => node.Role == "password text" && node.Name == "Password:"));
        Assert.Contains(prompt, node => node.Role == "password text" && node.Name == "Password:");
        await greeter.SendAsync("type hunter2");
        await greeter.SettleAsync();
        var texts = await a11y.TextsAsync();
        var tree = await a11y.TreeAsync();
        Assert.Contains("●●●●●●●", texts);
        Assert.DoesNotContain(texts, text => text.Contains("hunter2", StringComparison.Ordinal));
        Assert.DoesNotContain(tree, node => node.Name.Contains("hunter2", StringComparison.Ordinal));
    }
}
