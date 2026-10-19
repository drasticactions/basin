using Xunit;

namespace LunaWm.Tests;

public sealed class InspectorTests
{
    public static TheoryData<string> Rows() => new("gl", "vulkan");

    private static bool Starts(string line, string prefix) => line.StartsWith(prefix, StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Virtual_pointer_scroll_reaches_a_window_except_under_a_panel(string renderer)
    {
        using var config = new TempConfig();
        using var session = await GoldenTests.Start(renderer, config);
        var socket = await session.WaitForAsync(line => Starts(line, "SOCKET "));
        Assert.NotNull(socket);
        var (before, overPanel, overlay, after, where) = await Basin.Tests.InspectorScroll.Run(
            socket!.Split(' ')[1],
            (command, match) => session.WaitForAsync(match, poke: command, fresh: true),
            session.SendAsync,
            TestContext.Current.CancellationToken);
        Assert.True(before == 3 && overPanel == 0 && overlay == 3 && after == 3, $"{where}: before={before} over-panel={overPanel} overlay={overlay} after={after}");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_inspector_opens_on_the_output_and_a_click_closes_a_panel(string renderer)
    {
        using var config = new TempConfig();
        using var session = await GoldenTests.Start(renderer, config);

        await session.SendAsync("inspector open");
        var opened = await session.WaitForAsync(line => Starts(line, "INSPECTOR open=yes"));
        Assert.NotNull(opened);
        Assert.Contains($"host={renderer}", opened!, StringComparison.Ordinal);
        Assert.Contains("output=HEADLESS-1", opened, StringComparison.Ordinal);

        var mark = session.Mark();
        await session.SendAsync("inspector-panel windows on");
        var both = await session.WaitForAsync(line => Starts(line, "INSPECTOR "), since: mark);
        Assert.Contains("panels=Outputs,Windows", both!, StringComparison.Ordinal);
        await session.SettleAsync();

        await session.SendAsync("move 531 50");
        await session.SettleAsync();
        await session.SendAsync("button 272 true");
        await session.SendAsync("button 272 false");
        await session.SettleAsync();
        var state = await session.WaitForAsync(line => Starts(line, "INSPECTOR open=yes") && !line.Contains("Outputs", StringComparison.Ordinal), poke: "inspector-state", fresh: true);
        Assert.NotNull(state);
        Assert.Contains("panels=Windows", state!, StringComparison.Ordinal);

        await session.SendAsync("inspector-panel outputs on");
        await Task.Delay(600, TestContext.Current.CancellationToken);
        await session.SendAsync("ipc input/touch {\"kind\":\"down\",\"id\":1,\"x\":531,\"y\":50}");
        await session.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await session.SendAsync("ipc input/touch {\"kind\":\"up\",\"id\":1}");
        await session.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        var touched = await session.WaitForAsync(line => Starts(line, "INSPECTOR open=yes") && !line.Contains("Outputs", StringComparison.Ordinal), poke: "inspector-state", fresh: true);
        Assert.True(touched is not null, "a touch on the close box did not close Outputs");

        mark = session.Mark();
        await session.SendAsync("inspector close");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "INSPECTOR open=no"), since: mark));

        mark = session.Mark();
        await session.SendAsync("key 125 true");
        await session.SendAsync("key 88 true");
        await session.SendAsync("key 88 false");
        await session.SendAsync("key 125 false");
        Assert.NotNull(await session.WaitForAsync(line => line == "ACTION inspector", since: mark));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "INSPECTOR open=yes"), since: mark));

        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }
}
