using Xunit;

namespace BasinGreeter.Tests;

public sealed class AccessibilityTests
{
    [Fact]
    public async Task High_contrast_and_text_size_apply_at_once_and_are_not_remembered()
    {
        using var fixture = new Fixture();
        using (var greeter = fixture.Start())
        {
            await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
            await greeter.SendAsync("contrast on");
            await greeter.ExpectAsync("CONTRAST on");
            await greeter.SendAsync("text-scale 2");
            await greeter.ExpectAsync("TEXTSCALE 200%");
            var mark = greeter.Mark();
            await greeter.ExpectAsync(line => line == "ACCESS contrast=on text=200% surface=640x360@2", "the box at half the logical size and twice the scale", poke: "where", since: mark);
            await greeter.SendAsync("text-scale 3");
            await greeter.ExpectAsync("TEXTSCALE 200%");
            await greeter.SendAsync("text-scale 1");
            await greeter.ExpectAsync("TEXTSCALE 100%");
            mark = greeter.Mark();
            await greeter.ExpectAsync(line => line == "ACCESS contrast=on text=100% surface=1280x720@1", "the box back at full size", poke: "where", since: mark);
        }

        using var again = fixture.Start();
        await again.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        var fresh = again.Mark();
        await again.ExpectAsync(line => line.StartsWith("ACCESS contrast=off text=100%", StringComparison.Ordinal), "the config defaults again", poke: "where", since: fresh);
    }

    [Fact]
    public async Task The_config_sets_the_defaults_and_a_reload_restores_them()
    {
        using var fixture = new Fixture("[accessibility]\nhigh_contrast = true\ntext_scale = 1.25\n");
        using var greeter = fixture.Start();
        await greeter.ExpectAsync("CONTRAST on");
        await greeter.ExpectAsync("TEXTSCALE 125%");
        await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        await greeter.SendAsync("contrast off");
        await greeter.ExpectAsync("CONTRAST off");
        var mark = greeter.Mark();
        await greeter.SendAsync("reload");
        await greeter.ExpectAsync(line => line == "CONTRAST on", "the default back after a reload", since: mark);
    }
}
