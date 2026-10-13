using Xunit;

namespace LunaWm.Tests;

public sealed class LogoutTests
{
    private static string Ssdwin()
    {
        var ssdwin = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "wlclients", "bin", "ssdwin");
        Assert.SkipUnless(File.Exists(ssdwin), "ssdwin has not been built");
        return ssdwin;
    }

    [Fact]
    public async Task A_retaining_exit_shrinks_the_cards_away_and_leaves_the_wallpaper_and_the_status_bar()
    {
        var ssdwin = Ssdwin();
        using var config = new TempConfig();
        var before = Path.Combine(config.Directory, "before.png");
        var after = Path.Combine(config.Directory, "after.png");
        using (var session = new LunaSession(LunaSession.Compositor(), config.Path, config.Environment, "-c", ssdwin, "--handoff", "retain", "--screenshot", after))
        {
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("CARD + 1 ", StringComparison.Ordinal)));
            await session.SettleAsync();
            await session.SendAsync($"shotraw {before}");
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith($"SHOTRAW {before} ", StringComparison.Ordinal)));
            var mark = session.Mark();
            await session.SendAsync("quit");
            Assert.NotNull(await session.WaitForAsync(line => line == "OUTRO logout HEADLESS-1", since: mark));
            Assert.True(session.WaitForExit(15_000), "luna-wm did not exit");
            Assert.Equal(0, session.Process.ExitCode);
        }

        Assert.True(ClientPixels(before) > 1000, "the card was not on screen before the exit");
        Assert.True(ClientPixels(after) < 100, "the card was still drawn in the last frame");
        Assert.True(BarPixels(after) > 20, "the status bar was not drawn in the last frame");
    }

    [Fact]
    public async Task An_exit_that_does_not_retain_runs_no_outro()
    {
        using var config = new TempConfig();
        using var session = new LunaSession(LunaSession.Compositor(), config.Path, config.Environment, "--handoff", "off");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        await session.SettleAsync();
        await session.SendAsync("quit");
        Assert.True(session.WaitForExit(15_000), "luna-wm did not exit");
        Assert.DoesNotContain(session.Lines(), line => line.StartsWith("OUTRO", StringComparison.Ordinal));
    }

    private static int ClientPixels(string path)
    {
        var (rgba, _, _) = Basin.Diagnostics.PngCodec.Decode(File.ReadAllBytes(path));
        var count = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (Math.Abs(rgba[i] - 0x3c) < 12 && Math.Abs(rgba[i + 1] - 0x7a) < 12 && Math.Abs(rgba[i + 2] - 0x9e) < 12)
            {
                count++;
            }
        }

        return count;
    }

    private static int BarPixels(string path)
    {
        var (rgba, width, _) = Basin.Diagnostics.PngCodec.Decode(File.ReadAllBytes(path));
        var count = 0;
        for (var i = 0; i < width * LunaView.StatusBarHeight * 4; i += 4)
        {
            if (rgba[i] > 200 && rgba[i + 1] > 200 && rgba[i + 2] > 200)
            {
                count++;
            }
        }

        return count;
    }
}
