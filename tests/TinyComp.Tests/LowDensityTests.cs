using Xunit;

namespace TinyComp.Tests;

public sealed class LowDensityTests
{
    private const string Config = """
        [compositor]
        scale = [2]

        [frame]
        style = "flat"

        [overview]
        hot_corner = "none"

        [[launch]]
        exec = "xprop"
        x11 = "low-density"
        """;

    private static bool WaitForFile(string path, string fragment, int timeoutMillis = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMillis;
        while (Environment.TickCount64 < deadline)
        {
            if (File.Exists(path) && File.ReadAllText(path).Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    [Fact]
    public void A_launch_rule_starts_the_init_command_on_the_low_density_server()
    {
        var output = Path.Combine(Path.GetTempPath(), $"basin-lowdensity-{Guid.NewGuid():N}.txt");
        try
        {
            using var session = EmulationTests.Session.Start(
                string.Empty, 0, Config, $"xprop -root RESOURCE_MANAGER > '{output}' 2>&1; echo \"DISPLAY $DISPLAY\" >> '{output}'");
            Assert.SkipWhen(session is null, "tinycomp or Xwayland is not available");
            var native = session!.WaitForLine("XWAYLAND :");
            var low = session.WaitForLine("XWAYLAND LOW ");
            Assert.NotNull(native);
            Assert.NotNull(low);
            Assert.True(WaitForFile(output, "DISPLAY "), File.Exists(output) ? File.ReadAllText(output) : "no output");
            var text = File.ReadAllText(output);
            Assert.Contains("Xft.dpi:\\t96", text, StringComparison.Ordinal);
            Assert.Contains($"DISPLAY {low!["XWAYLAND LOW ".Length..]}", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void An_ipc_spawn_without_a_density_follows_the_launch_rules()
    {
        var matched = Path.Combine(Path.GetTempPath(), $"basin-lowdensity-{Guid.NewGuid():N}.txt");
        var other = Path.Combine(Path.GetTempPath(), $"basin-lowdensity-{Guid.NewGuid():N}.txt");
        try
        {
            using var session = EmulationTests.Session.Start(string.Empty, 0, Config, "true");
            Assert.SkipWhen(session is null, "tinycomp or Xwayland is not available");
            var native = session!.WaitForLine("XWAYLAND :")!["XWAYLAND ".Length..];
            var low = session.WaitForLine("XWAYLAND LOW ")!["XWAYLAND LOW ".Length..];

            session.Send($$"""ipc process/spawn {"argv":["xprop","-root","RESOURCE_MANAGER"],"log":"{{matched}}"}""");
            session.Send($$"""ipc process/spawn {"argv":["/bin/sh","-c","echo DISPLAY $DISPLAY LOW $BASIN_X11_LOW_DENSITY_DISPLAY"],"log":"{{other}}"}""");
            Assert.True(WaitForFile(matched, "Xft.dpi"), File.Exists(matched) ? File.ReadAllText(matched) : "no output");
            Assert.Contains("Xft.dpi:\\t96", File.ReadAllText(matched), StringComparison.Ordinal);
            Assert.True(WaitForFile(other, "DISPLAY"));
            Assert.Equal($"DISPLAY {native} LOW {low}", File.ReadAllText(other).Trim());
        }
        finally
        {
            File.Delete(matched);
            File.Delete(other);
        }
    }

    [Fact]
    public void Set_x11_density_writes_a_rule_and_restarts_a_tracked_application_on_the_other_server()
    {
        using var session = EmulationTests.Session.Start(string.Empty, 0, Config, "true");
        Assert.SkipWhen(session is null, "tinycomp or Xwayland is not available");
        Assert.NotNull(session!.WaitForLine("XWAYLAND LOW "));

        session.Send("""ipc process/spawn {"argv":["xeyes","-geometry","200x100"]}""");
        var id = WaitForWindow(session, "XEyes", "native");
        Assert.NotNull(id);

        session.Send($$"""ipc windows/set-x11-density {"id":{{id}},"density":"low"}""");
        var answer = session.WaitForLine("OK windows/set-x11-density ", 15000);
        Assert.NotNull(answer);
        Assert.Contains("outcome=relaunched", answer, StringComparison.Ordinal);
        Assert.Contains("rule=exec=xeyes x11=low-density", answer, StringComparison.Ordinal);
        Assert.NotNull(WaitForWindow(session, "XEyes", "low"));
        Assert.Contains("[[launch]]", File.ReadAllText(session.ConfigPath), StringComparison.Ordinal);
        Assert.StartsWith(Config, File.ReadAllText(session.ConfigPath), StringComparison.Ordinal);
    }

    private static string? WaitForWindow(EmulationTests.Session session, string appId, string density)
    {
        var deadline = Environment.TickCount64 + 30000;
        var pattern = new System.Text.RegularExpressions.Regex(
            $"\\{{\"id\":(\\d+),\"title\":\"[^\"]*\",\"app_id\":\"{appId}\".*?\"x11_density\":\"{density}\"");
        while (Environment.TickCount64 < deadline)
        {
            session.Send("ipc windows/list {}");
            if (session.WaitForLine("OK windows/list ") is { } line && pattern.Match(line) is { Success: true } match)
            {
                return match.Groups[1].Value;
            }

            Thread.Sleep(300);
        }

        return null;
    }
}
