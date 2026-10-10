using Xunit;

namespace System7Wm.Tests;

public sealed class VersionSplitTests
{
    public static TheoryData<string> Renderers => ["pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite"];

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task A_menu_a_pop_up_and_a_text_field_work_on_the_nightly(string renderer)
    {
        Prerequisites.SkipUnlessRenderer(renderer);
        using var session = new Session(Prerequisites.Compositor(), renderer);
        File.WriteAllText(Path.Combine(session.Home, "Desktop", "a.txt"), "a");
        var chosen = await session.WaitForPrefixAsync("RENDERER ");
        Assert.NotNull(chosen);
        Assert.SkipWhen(chosen!.Split(' ')[1] != renderer, $"{renderer} fell back to {chosen.Split(' ')[1]}");
        await session.DisplayAsync();

        await session.SendAsync("move 20 10");
        await session.SendAsync("button 272 1");
        Assert.NotNull(await session.WaitForLineAsync("MENUBAR open"));
        await session.SendAsync("button 272 0");
        Assert.NotNull(await session.WaitForLineAsync("MENUBAR closed"));

        await session.SendAsync("panel Sound");
        Assert.NotNull(await session.WaitForLineAsync("PANEL Sound open"));
        await session.SendAsync("ui-click panel:Sound alertSound");
        Assert.NotNull(await session.WaitForLineAsync("CLICK panel:Sound alertSound"));
        var grown = await session.WaitForAsync(line => line.StartsWith("SHELL ", StringComparison.Ordinal) && line.EndsWith("grown=True", StringComparison.Ordinal), poke: "where", fresh: true, attempts: 50);
        Assert.True(grown is not null, await session.StateLineAsync("SHELL "));
        await session.SendAsync("key 108 1");
        await session.SendAsync("key 108 0");
        await session.SendAsync("key 28 1");
        await session.SendAsync("key 28 0");
        Assert.NotNull(await session.WaitForPrefixAsync("ALERTSOUND "));
        await session.SettleAsync();
        Assert.EndsWith("grown=False", (await session.StateLineAsync("SHELL "))!, StringComparison.Ordinal);
        await session.SendAsync("panel Sound close");
        Assert.NotNull(await session.WaitForLineAsync("PANEL Sound closed"));

        var icon = await session.WaitForAsync(line => line.StartsWith("ICON File \"a.txt\" ", StringComparison.Ordinal), poke: "desktop");
        Assert.NotNull(icon);
        var position = icon!.Split(' ')[3].Split(',');
        var x = int.Parse(position[0], System.Globalization.CultureInfo.InvariantCulture) + 32;
        var y = int.Parse(position[1], System.Globalization.CultureInfo.InvariantCulture) + 40;
        await session.SendAsync($"move {x} {y}");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        await Task.Delay(800, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        foreach (var code in new[] { 48, 23 })
        {
            await session.SendAsync($"key {code} 1");
            await session.SendAsync($"key {code} 0");
        }

        await session.SendAsync("key 28 1");
        await session.SendAsync("key 28 0");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("RENAME ", StringComparison.Ordinal) && line.EndsWith("/Desktop/bi", StringComparison.Ordinal)));
        Assert.True(File.Exists(Path.Combine(session.Home, "Desktop", "bi")));
        await session.QuitCleanlyAsync();
    }
}
