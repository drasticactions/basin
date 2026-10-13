using Xunit;

namespace EightWm.Tests;

public sealed class SignOutTests
{
    private static readonly Dictionary<string, string> Quiet = new() { ["BASIN_MUTE"] = "1" };

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task A_retaining_exit_slides_the_sign_out_screen_in_and_leaves_it_lit(string renderer)
    {
        EightGoldens.SkipUnlessRenderer(renderer);
        var shot = Path.Combine(Path.GetTempPath(), $"eight-wm-sign-out-{Environment.ProcessId}-{renderer}.png");
        try
        {
            using (var session = new EightSession(renderer, EightSession.Compositor(), "false", Quiet, "--ipc", "false", "--handoff", "retain", "--screenshot", shot))
            {
                await EightGoldens.RequireRenderer(session, renderer);
                Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
                await session.SettleAsync();
                var mark = session.Mark();
                await session.SendAsync("quit");
                Assert.NotNull(await session.WaitForAsync(line => line == "OUTRO sign-out HEADLESS-1", since: mark));
                Assert.True(session.WaitForExit(15_000), "eight-wm did not exit");
                Assert.Equal(0, session.Process.ExitCode);
            }

            EightGoldens.MatchFile(shot, "sign-out", renderer);
        }
        finally
        {
            File.Delete(shot);
        }
    }

    [Fact]
    public async Task An_exit_that_does_not_retain_shows_no_sign_out_screen()
    {
        using var session = new EightSession(EightSession.Compositor(), "false", Quiet, "--ipc", "false", "--handoff", "off");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        await session.SettleAsync();
        await session.SendAsync("quit");
        Assert.True(session.WaitForExit(15_000), "eight-wm did not exit");
        Assert.DoesNotContain(session.Lines(), line => line.StartsWith("OUTRO", StringComparison.Ordinal));
    }
}
