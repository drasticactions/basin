using System.Globalization;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace System7Wm.Tests;

public sealed class GoldenTests
{
    public static TheoryData<string, string> Rows()
    {
        var rows = new TheoryData<string, string>();
        foreach (var renderer in new[] { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" })
        {
            foreach (var scale in new[] { "1", "1.5", "1.7", "2" })
            {
                rows.Add(renderer, scale);
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_desktop_the_drag_outline_and_an_open_menu_match_their_goldens(string renderer, string scale)
    {
        Prerequisites.SkipUnlessRenderer(renderer);
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var config = Path.Combine(Path.GetTempPath(), $"system7-wm-golden-{Environment.ProcessId}.toml");
        File.WriteAllText(config, "[finder]\ndisk_name = \"Macintosh HD\"\n");
        using var session = new Session(Prerequisites.Compositor(), renderer, null, "--scale", scale, "--config", config);
        Directory.CreateDirectory(Path.Combine(session.Home, "Desktop", "Projects"));
        var chosen = await session.WaitForPrefixAsync("RENDERER ");
        Assert.SkipWhen(chosen!.Split(' ')[1] != renderer, $"{renderer} fell back to {chosen.Split(' ')[1]}");
        var display = await session.DisplayAsync();
        await session.SendAsync("clock 12:00");

        var clients = new List<Client>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var mark = session.Mark();
                clients.Add(session.StartClient(ssdwin, display, null, "server"));
                Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("WINDOW + ", StringComparison.Ordinal), since: mark));
            }

            var windows = (await session.StateAsync()).Where(line => line.StartsWith("WINDOW ", StringComparison.Ordinal)).ToList();
            Assert.Equal(3, windows.Count);
            var first = windows[0].Split(' ')[1];
            var active = windows.Single(line => line.Contains("active=True", StringComparison.Ordinal));
            var box = active.Split(' ')[4].Split(',', 'x').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();

            await session.SendAsync($"shade {first}");
            Assert.NotNull(await session.WaitForLineAsync($"SHADE {first} collapsed"));
            await session.SendAsync("move 5000 5000");
            await Shoot(session, "desktop", renderer, scale);

            var x = box[0] + 60;
            var y = box[1] - 9;
            await session.SendAsync($"move {x} {y}");
            await session.SendAsync("button 272 1");
            foreach (var step in new[] { 10, 30, 50 })
            {
                await session.SendAsync($"move {x + step} {y + step}");
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            await Shoot(session, "drag", renderer, scale);
            await session.SendAsync("button 272 0");
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MOVE ", StringComparison.Ordinal)));

            await session.SendAsync("move 60 10");
            await session.SendAsync("button 272 1");
            Assert.NotNull(await session.WaitForLineAsync("MENUBAR open"));
            await Shoot(session, "menu", renderer, scale);
            await session.SendAsync("button 272 0");
            Assert.NotNull(await session.WaitForLineAsync("MENUBAR closed"));
            await session.QuitCleanlyAsync();
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    public static TheoryData<string, string> TerminalRows()
    {
        var rows = new TheoryData<string, string>();
        foreach (var renderer in new[] { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" })
        {
            foreach (var scale in new[] { "1", "2" })
            {
                rows.Add(renderer, scale);
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(TerminalRows))]
    public async Task A_terminal_window_matches_its_golden(string renderer, string scale)
    {
        Prerequisites.SkipUnlessRenderer(renderer);
        var config = Path.Combine(Path.GetTempPath(), $"system7-wm-golden-terminal-{Environment.ProcessId}.toml");
        File.WriteAllText(config, "[finder]\ndisk_name = \"Macintosh HD\"\n");
        using var session = new Session(Prerequisites.Compositor(), renderer, null, "--scale", scale, "--config", config);
        var chosen = await session.WaitForPrefixAsync("RENDERER ");
        Assert.SkipWhen(chosen!.Split(' ')[1] != renderer, $"{renderer} fell back to {chosen.Split(' ')[1]}");
        await session.DisplayAsync();
        await session.SendAsync("clock 12:00");
        await session.SendAsync("move 5000 5000");
        var text = Path.Combine(session.Home, "screen.txt");
        File.WriteAllText(text,
            "\u001b]0;Terminal\u0007Welcome to Macintosh.\r\n" +
            "\u001b[1mbold\u001b[0m \u001b[4munderline\u001b[0m \u001b[7mreverse\u001b[0m\r\n" +
            "\u001b[31mred\u001b[0m \u001b[32mgreen\u001b[0m \u001b[34mblue\u001b[0m \u001b[43myellow\u001b[0m\r\n" +
            "$ ls -l /System\r\n" +
            "drwxr-xr-x  3 mac  staff  96 Jan 24  1984 Finder\r\n" +
            "-rw-r--r--  1 mac  staff  42 May 13  1991 System\r\n$ ");
        var mark = session.Mark();
        await session.SendAsync($"terminal sh -c 'cat {text}; exec sleep 60'");
        Assert.NotNull(await session.WaitForPrefixAsync("TERMINAL + ", mark));
        Assert.NotNull(await session.WaitForLineAsync("TERMINAL title 1 \"Terminal\"", mark));
        await Shoot(session, "terminal", renderer, scale);
        await session.QuitCleanlyAsync();
    }

    private static async Task Shoot(Session session, string scene, string renderer, string scale)
    {
        await session.SettleAsync();
        var path = Path.Combine(session.Home, $"{scene}.png");
        var mark = session.Mark();
        await session.SendAsync($"shotraw {path}");
        var reply = await session.WaitForPrefixAsync("SHOTRAW ", mark);
        Assert.True(reply is not null && reply.StartsWith($"SHOTRAW {path} ", StringComparison.Ordinal), $"[{reply}] [{path}]");
        for (var i = 0; i < 50 && !File.Exists(path); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        AssertMatches(path, $"{scene}-{scale}", renderer == "pixman" ? 0 : 2, record: renderer == "pixman");
    }

    private static void AssertMatches(string actualPath, string name, int tolerance, bool record, [CallerFilePath] string sourcePath = "")
    {
        var goldenPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "Goldens", $"{name}.png");
        var actualPng = File.ReadAllBytes(actualPath);
        if (record && Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllBytes(goldenPath, actualPng);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            throw new FileNotFoundException($"Golden '{name}' missing. Run once with BASIN_UPDATE_GOLDENS=1 and commit {goldenPath}.");
        }

        var (expected, width, height) = PngCodec.Decode(File.ReadAllBytes(goldenPath));
        var (actual, actualWidth, actualHeight) = PngCodec.Decode(actualPng);
        Assert.True(width == actualWidth && height == actualHeight, $"Golden '{name}' is {width}x{height}, the screenshot is {actualWidth}x{actualHeight}.");

        var differing = 0;
        var maxDelta = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var delta = 0;
            for (var channel = 0; channel < 3; channel++)
            {
                delta = Math.Max(delta, Math.Abs(expected[i + channel] - actual[i + channel]));
            }

            differing += delta > tolerance ? 1 : 0;
            maxDelta = Math.Max(maxDelta, delta);
        }

        if (differing > 0)
        {
            var kept = Path.Combine(Path.GetTempPath(), $"system7-wm-golden-{name}-actual.png");
            File.Copy(actualPath, kept, overwrite: true);
            Assert.Fail($"Golden '{name}' mismatch: {differing} pixels beyond ±{tolerance} (max channel delta {maxDelta}). Actual kept at {kept}.");
        }
    }
}
