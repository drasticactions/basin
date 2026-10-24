using Xunit;
using static TabWm.Tests.Prerequisites;

namespace TabWm.Tests;

public sealed class IdleTests
{
    [Theory]
    [InlineData("managed")]
    [InlineData("auto")]
    public async Task An_idle_desktop_with_a_strip_a_launcher_and_a_client_draws_no_frames(string engine)
    {
        var tabwin = WlClient("tabwin");
        using var session = new Session(Compositor(), extra: ["--no-xwayland", "--no-restore", "--engine", engine]);
        await session.DisplayAsync();
        var union = await session.WaitForPrefixAsync("UNION ");
        Assert.NotNull(union);
        if (engine == "managed")
        {
            Assert.Equal("managed", Field(union!, "engine"));
        }
        else
        {
            Assert.SkipWhen(Field(union!, "engine") != "native", "KDE Union is not installed, so the native engine cannot run");
            Assert.Equal("shared", Field(union!, "pump"));
            Assert.Equal("yes", Field(union!, "glib"));
        }

        Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER "));
        await session.SendAsync("clock 12:00");
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin} --title idle");
        Assert.NotNull(await session.WaitForPrefixAsync("PLACED ", mark, 20_000));
        await session.AskAsync("tab new", "TAB ");
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        var before = await session.FrameCountAsync();
        await Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        var after = await session.FrameCountAsync();
        Assert.Equal(before, after);

        var glib = await session.AskAsync("glib", "GLIB ");
        Assert.Equal("no", Field(Assert.Single(glib), "armed"));
        await session.QuitCleanlyAsync();
    }
}
