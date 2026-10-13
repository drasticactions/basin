using Basin.Freedesktop;
using Basin.Greetd;
using Xunit;

namespace Basin.Tests;

public sealed class GreetdSessionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basin-greetd-sessions-" + Guid.NewGuid().ToString("N"));

    public GreetdSessionsTests()
    {
        Write("share/wayland-sessions/sway.desktop", "[Desktop Entry]\nType=Application\nName=Sway\nExec=sway --unsupported-gpu\nDesktopNames=sway;wlroots;\n");
        Write("share/wayland-sessions/gone.desktop", "[Desktop Entry]\nType=Application\nName=Gone\nExec=gone\nTryExec=/nonexistent/gone\n");
        Write("share/wayland-sessions/hidden.desktop", "[Desktop Entry]\nType=Application\nName=Hidden\nExec=hidden\nHidden=true\n");
        Write("share/wayland-sessions/quoted.desktop", "[Desktop Entry]\nType=Application\nName=Quoted\nExec=sh -c \"exec my-session --flag 'two words'\"\n");
        Write("share/xsessions/i3.desktop", "[Desktop Entry]\nType=Application\nName=i3\nExec=i3\nDesktopNames=i3\n");
        Write("share/xsessions/sway.desktop", "[Desktop Entry]\nType=Application\nName=Sway on X\nExec=sway\n");
        Write("environments", "# a comment\nbash\n\nfish -l\n");
    }

    private GreetdSessionsOptions Options(IReadOnlyList<string>? wrapper = null) => new()
    {
        DataDirectories = [Path.Combine(_root, "share")],
        EnvironmentsPath = Path.Combine(_root, "environments"),
        X11Wrapper = wrapper ?? ["startx"],
        Locale = DesktopLocale.None,
    };

    [Fact]
    public void Wayland_comes_first_then_x11_then_the_environments_file()
    {
        var sessions = GreetdSessions.List(Options());
        Assert.Equal(["quoted", "sway", "i3", "bash", "fish -l"], sessions.Select(s => s.Id));
        Assert.Equal([GreetdSessionType.Wayland, GreetdSessionType.Wayland, GreetdSessionType.X11, GreetdSessionType.Command, GreetdSessionType.Command], sessions.Select(s => s.Type));
        Assert.Equal("Sway", sessions[1].Name);
    }

    [Fact]
    public void Desktop_names_reach_the_environment_and_the_command_carries_it_through_env()
    {
        var sway = GreetdSessions.List(Options()).Single(s => s.Id == "sway");
        var command = sway.Command(["XKB_DEFAULT_LAYOUT=de"]);
        Assert.Equal(["XDG_SESSION_TYPE=wayland", "XDG_CURRENT_DESKTOP=sway:wlroots", "XDG_SESSION_DESKTOP=sway", "XKB_DEFAULT_LAYOUT=de"], command.Env);
        Assert.Equal(["/usr/bin/env", "XDG_SESSION_TYPE=wayland", "XDG_CURRENT_DESKTOP=sway:wlroots", "XDG_SESSION_DESKTOP=sway", "XKB_DEFAULT_LAYOUT=de", "sway", "--unsupported-gpu"], command.Cmd);
    }

    [Fact]
    public void An_x11_session_runs_under_the_wrapper()
    {
        var i3 = GreetdSessions.List(Options(["startx", "/usr/bin/env"])).Single(s => s.Id == "i3");
        Assert.Equal(["startx", "/usr/bin/env", "/usr/bin/env", "XDG_SESSION_TYPE=x11", "XDG_CURRENT_DESKTOP=i3", "XDG_SESSION_DESKTOP=i3", "i3"], i3.Command().Cmd);
    }

    [Fact]
    public void An_empty_wrapper_hides_x11_sessions()
    {
        Assert.DoesNotContain(GreetdSessions.List(Options([])), s => s.Type == GreetdSessionType.X11);
    }

    [Fact]
    public void Exec_quoting_survives_the_join_greetd_does()
    {
        var quoted = GreetdSessions.List(Options()).Single(s => s.Id == "quoted");
        Assert.Equal(["sh", "-c", "exec my-session --flag 'two words'"], quoted.Argv);
        var cmd = quoted.Command().Cmd;
        Assert.Equal(["sh", "-c", "exec my-session --flag 'two words'"], ExecLine.Split(string.Join(' ', cmd.Skip(2))));
    }

    [Fact]
    public void An_environments_line_is_one_command_element()
    {
        var fish = GreetdSessions.List(Options()).Single(s => s.Id == "fish -l");
        Assert.Equal(["fish -l"], fish.Command().Cmd);
        Assert.Empty(fish.Command().Env);
        Assert.Equal(["my shell"], GreetdSessions.FreeCommand("my shell").Command().Cmd);
    }

    [Fact]
    public void A_missing_environments_file_and_missing_directories_give_an_empty_list()
    {
        var sessions = GreetdSessions.List(new GreetdSessionsOptions { DataDirectories = [Path.Combine(_root, "none")], EnvironmentsPath = Path.Combine(_root, "none/environments"), Locale = DesktopLocale.None });
        Assert.Empty(sessions);
    }

    public void Dispose() => Directory.Delete(_root, true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
