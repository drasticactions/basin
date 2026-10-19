using Xunit;

namespace MauiComp.Tests;

public sealed class InspectorTests
{
    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public async Task Virtual_pointer_scroll_reaches_a_window_except_under_a_panel(string renderer)
    {
        var (compositor, _) = HeadlessSessionTests.Require();
        Assert.SkipWhen(!File.Exists("/dev/dri/renderD128"), "no render node");
        using var session = new Session(compositor, renderer);
        var display = await session.DisplayAsync();
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
    public async Task The_inspector_opens_and_a_click_closes_a_panel(string renderer)
    {
        var (compositor, _) = HeadlessSessionTests.Require();
        Assert.SkipWhen(!File.Exists("/dev/dri/renderD128"), "no render node");
        using var session = new Session(compositor, renderer);
        await session.DisplayAsync();
        var chosen = await session.WaitForAsync(line => line.StartsWith("RENDERER ", StringComparison.Ordinal));
        Assert.NotNull(chosen);
        Assert.SkipWhen(chosen!.Split(' ')[1] != renderer, $"{renderer} fell back to {chosen.Split(' ')[1]}");
        await session.SettleAsync();

        var mark = session.Mark();
        await session.SendAsync("inspector open");
        var opened = await session.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal), since: mark);
        Assert.NotNull(opened);
        Assert.StartsWith("INSPECTOR open=yes", opened!, StringComparison.Ordinal);
        Assert.Contains($"host={renderer}", opened, StringComparison.Ordinal);

        mark = session.Mark();
        await session.SendAsync("inspector-panel windows on");
        var both = await session.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal), since: mark);
        Assert.Contains("panels=Outputs,Windows", both!, StringComparison.Ordinal);
        await session.SettleAsync();

        await session.SendAsync("move 531 50");
        await session.SettleAsync();
        await session.SendAsync("button 272 true");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 false");
        await session.SettleAsync();

        var state = await session.WaitForAsync(
            line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal) && line.Contains("panels=Windows", StringComparison.Ordinal),
            poke: "inspector-state", fresh: true);
        Assert.NotNull(state);

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
            poke: "inspector-state", fresh: true);
        Assert.True(touched is not null, "a touch on the close box did not close Outputs");

        mark = session.Mark();
        await session.SendAsync("inspector close");
        var closed = await session.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal), since: mark);
        Assert.StartsWith("INSPECTOR open=no", closed!, StringComparison.Ordinal);

        mark = session.Mark();
        await session.SendAsync("key 125 true");
        await session.SendAsync("key 88 true");
        await session.SendAsync("key 88 false");
        await session.SendAsync("key 125 false");
        var chord = await session.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal), since: mark);
        Assert.NotNull(chord);
        Assert.StartsWith("INSPECTOR open=yes", chord!, StringComparison.Ordinal);
        await HeadlessSessionTests.AssertCleanExit(session);
    }
}
