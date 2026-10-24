using Xunit;

namespace TabWm.Tests;

public sealed class SessionFileTests
{
    [Fact]
    public void A_session_round_trips_through_its_file()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("tab-wm-session-").FullName, "session.json");
        var file = new SessionFile();
        file.Outputs.Add(new SessionOutput
        {
            Connector = "DP-2",
            Selected = 1,
            Tabs =
            [
                new SessionTab { Label = "Mail", Apps = [new SessionApp { DesktopId = "org.kde.kmail2.desktop" }] },
                new SessionTab { Muted = true, Apps = [new SessionApp { Command = "foot -e htop" }] },
            ],
        });

        Assert.True(StateFiles.Write(path, file, TabWmJsonContext.Default.SessionFile, out var error), error);
        var read = StateFiles.Read(path, TabWmJsonContext.Default.SessionFile, out error);
        Assert.Null(error);
        Assert.True(TabSession.IsValid(read));
        var output = Assert.Single(read!.Outputs);
        Assert.Equal("DP-2", output.Connector);
        Assert.Equal(1, output.Selected);
        Assert.Equal("Mail", output.Tabs[0].Label);
        Assert.Equal("org.kde.kmail2.desktop", output.Tabs[0].Apps[0].DesktopId);
        Assert.True(output.Tabs[1].Muted);
        Assert.Equal("foot -e htop", output.Tabs[1].Apps[0].Command);
        Assert.Contains("\"desktop_id\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_corrupt_file_reports_an_error_and_no_session()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("tab-wm-session-").FullName, "session.json");
        File.WriteAllText(path, "{ \"outputs\": [ { \"connector\": ");
        var read = StateFiles.Read(path, TabWmJsonContext.Default.SessionFile, out var error);
        Assert.Null(read);
        Assert.NotNull(error);
    }

    [Fact]
    public void A_missing_file_is_no_session_and_no_error()
    {
        var read = StateFiles.Read("/nonexistent/tab-wm/session.json", TabWmJsonContext.Default.SessionFile, out var error);
        Assert.Null(read);
        Assert.Null(error);
    }

    [Fact]
    public void A_future_version_is_not_valid() =>
        Assert.False(TabSession.IsValid(new SessionFile { Version = 2 }));
}
