using Xunit;

namespace EightWm.Tests;

public sealed class InspectorTests
{
    public static TheoryData<string> Rows => new() { "gl", "vulkan" };

    private static Dictionary<string, string> Environment() => new()
    {
        ["BASIN_MUTE"] = "1",
        ["BASIN_STILL_CARET"] = "1",
        ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-inspector-tests/session",
        ["DBUS_SYSTEM_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-inspector-tests/system",
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Virtual_pointer_scroll_reaches_a_window_except_under_a_panel(string row)
    {
        EightGoldens.SkipUnlessRenderer(row);
        var config = Path.Combine(Path.GetTempPath(), $"eight-inspector-{Guid.NewGuid():N}.toml");
        File.WriteAllText(config, "[start]\nscan_desktop_files = false\n");
        try
        {
            using var session = new EightSession(row, EightSession.Compositor(), config, Environment());
            await EightGoldens.RequireRenderer(session, row);
            var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
            Assert.NotNull(socket);
            await session.SettleAsync();
            var (before, overPanel, overlay, after, where) = await Basin.Tests.InspectorScroll.Run(
                socket!.Split(' ')[1],
                (command, match) => session.WaitForAsync(match, poke: command, fresh: true),
                session.SendAsync,
                TestContext.Current.CancellationToken);
            Assert.True(before == 3 && overPanel == 0 && overlay == 3 && after == 3, $"{where}: before={before} over-panel={overPanel} overlay={overlay} after={after}");
        }
        finally
        {
            File.Delete(config);
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_inspector_opens_takes_a_click_and_toggles_on_its_chord(string row)
    {
        EightGoldens.SkipUnlessRenderer(row);
        var config = Path.Combine(Path.GetTempPath(), $"eight-inspector-{Guid.NewGuid():N}.toml");
        File.WriteAllText(config, "[start]\nscan_desktop_files = false\n");
        try
        {
            using var session = new EightSession(row, EightSession.Compositor(), config, Environment());
            await EightGoldens.RequireRenderer(session, row);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SettleAsync();

            var mark = session.Mark();
            await session.SendAsync("inspector open");
            var opened = await session.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal), since: mark);
            Assert.NotNull(opened);
            Assert.StartsWith("INSPECTOR open=yes", opened!, StringComparison.Ordinal);
            Assert.Contains($"host={row}", opened, StringComparison.Ordinal);

            mark = session.Mark();
            await session.SendAsync("inspector-panel windows on");
            Assert.NotNull(await session.WaitForAsync(
                line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal) && line.Contains("panels=Outputs,Windows", StringComparison.Ordinal),
                since: mark));
            await session.SettleAsync();

            await session.SendAsync("move 531 50");
            await session.SettleAsync();
            await session.SendAsync("mousedown");
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await session.SendAsync("mouseup");
            var closed = await session.WaitForAsync(
                line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal) && line.Contains("panels=Windows", StringComparison.Ordinal),
                poke: "inspector-state", fresh: true, attempts: 40);
            Assert.True(closed is not null, $"the Outputs panel did not close: {string.Join(" | ", session.Lines().Where(l => l.StartsWith("INSPECTOR", StringComparison.Ordinal)).TakeLast(3))}");

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
                poke: "inspector-state", fresh: true, attempts: 40);
            Assert.True(touched is not null, "a touch on the close box did not close Outputs");

            mark = session.Mark();
            await session.SendAsync("inspector close");
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("INSPECTOR open=no", StringComparison.Ordinal), since: mark));

            mark = session.Mark();
            await session.SendAsync("key Super+F12");
            Assert.NotNull(await session.WaitForAsync(line => line == "ACTION inspector", since: mark));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("INSPECTOR open=yes", StringComparison.Ordinal), since: mark));
        }
        finally
        {
            File.Delete(config);
        }
    }
}
