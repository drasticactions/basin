using Xunit;

namespace System7Wm.Tests;

public sealed class InspectorTests
{
    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public async Task Virtual_pointer_scroll_reaches_a_window_except_under_a_panel(string renderer)
    {
        Prerequisites.SkipUnlessRenderer(renderer);
        using var session = new Session(Prerequisites.Compositor(), renderer);
        var display = await session.DisplayAsync();
        await session.SettleAsync();
        var (before, overPanel, overlay, after, where) = await Basin.Tests.InspectorScroll.Run(
            display,
            (command, match) => session.WaitForAsync(match, poke: command, fresh: true),
            session.SendAsync,
            TestContext.Current.CancellationToken);
        Assert.True(before == 3 && overPanel == 0 && overlay == 3 && after == 3, $"{where}: before={before} over-panel={overPanel} overlay={overlay} after={after}");
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public async Task The_inspector_opens_takes_a_click_on_its_panel_and_closes(string renderer)
    {
        Prerequisites.SkipUnlessRenderer(renderer);
        using var session = new Session(Prerequisites.Compositor(), renderer);
        await session.DisplayAsync();
        await session.SettleAsync();

        await session.SendAsync("inspector open");
        var opened = await session.WaitForPrefixAsync("INSPECTOR ");
        Assert.SkipWhen(opened is not null && opened.Contains("host=none", StringComparison.Ordinal), $"no ImGui host on {renderer}: {opened}");
        Assert.NotNull(opened);
        Assert.StartsWith("INSPECTOR open=yes", opened!, StringComparison.Ordinal);
        Assert.Contains($"host={renderer}", opened, StringComparison.Ordinal);

        var mark = session.Mark();
        await session.SendAsync("inspector-panel windows on");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal) && line.Contains("panels=Outputs,Windows", StringComparison.Ordinal), since: mark));
        await session.SettleAsync();

        await session.SendAsync("move 531 50");
        await session.SettleAsync();
        await session.SendAsync("button 272 true");
        await session.SendAsync("button 272 false");
        await session.SettleAsync();

        var state = await session.WaitForAsync(
            line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal) && line.Contains("panels=Windows", StringComparison.Ordinal),
            poke: "inspector-state", fresh: true, attempts: 30);
        Assert.True(state is not null, $"the close box did not close Outputs: {string.Join(" | ", session.LinesSince(mark).Where(l => l.StartsWith("INSPECTOR", StringComparison.Ordinal)))}");

        await session.SendAsync("inspector-panel outputs on");
        await session.SettleAsync();
        await session.SendAsync("ipc input/touch {\"kind\":\"down\",\"id\":1,\"x\":531,\"y\":50}");
        await session.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await session.SendAsync("ipc input/touch {\"kind\":\"up\",\"id\":1}");
        await session.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await session.SettleAsync();
        var touched = await session.WaitForAsync(
            line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal) && line.Contains("panels=Windows", StringComparison.Ordinal),
            poke: "inspector-state", fresh: true, attempts: 30);
        Assert.True(touched is not null, "a touch on the close box did not close Outputs");

        mark = session.Mark();
        await session.SendAsync("inspector close");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("INSPECTOR open=no", StringComparison.Ordinal), since: mark));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task On_pixman_the_inspector_says_it_is_unavailable()
    {
        using var session = new Session(Prerequisites.Compositor());
        await session.DisplayAsync();
        await session.SendAsync("inspector open");
        Assert.NotNull(await session.WaitForPrefixAsync("ERR the inspector is unavailable"));
        await session.SendAsync("key 125 1");
        await session.SendAsync("key 88 1");
        await session.SendAsync("key 88 0");
        await session.SendAsync("key 125 0");
        Assert.NotNull(await session.WaitForLineAsync("CHORD inspector"));
        Assert.NotNull(await session.WaitForAsync(l => l.StartsWith("INSPECTOR open=no host=none", StringComparison.Ordinal)));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task Super_alt_8_zooms_and_super_alt_c_draws_the_screen_curtain()
    {
        using var session = new Session(Prerequisites.Compositor());
        await session.DisplayAsync();
        async Task AccessChord(int key)
        {
            foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (key, 1), (key, 0), (56, 0), (125, 0) })
            {
                await session.SendAsync($"key {code} {pressed}");
            }
        }

        await AccessChord(9);
        Assert.NotNull(await session.WaitForLineAsync("ZOOM factor=2"));
        await AccessChord(46);
        Assert.NotNull(await session.WaitForLineAsync("CURTAIN on"));
        await AccessChord(46);
        Assert.NotNull(await session.WaitForLineAsync("CURTAIN off"));
        await AccessChord(12);
        Assert.NotNull(await session.WaitForLineAsync("ZOOM factor=1.6"));
        await session.QuitCleanlyAsync();
    }
}
