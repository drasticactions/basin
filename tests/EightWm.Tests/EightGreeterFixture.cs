using Basin.Tests;

namespace EightWm.Tests;

internal sealed class EightGreeterFixture : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("eight-greeter-");

    public EightGreeterFixture(string config = "")
    {
        Write("root/etc/passwd", string.Join('\n',
            "root:x:0:0:root:/root:/bin/bash",
            "ada:x:1000:1000:Ada Lovelace:/home/ada:/bin/bash",
            "charles:x:1001:1001:Charles Babbage,,,:/home/charles:/bin/zsh",
            ""));
        Write("root/etc/login.defs", "UID_MIN 1000\nUID_MAX 60000\n");
        Write("root/usr/share/wayland-sessions/sway.desktop", "[Desktop Entry]\nType=Application\nName=Sway\nExec=sway\nDesktopNames=sway;wlroots;\n");
        Write("root/usr/share/wayland-sessions/tinycomp.desktop", "[Desktop Entry]\nType=Application\nName=TinyComp\nExec=tinycomp --config false\n");
        Write("environments", "");
        Rewrite(config);
        Directory.CreateDirectory(Path.Combine(_dir.FullName, "state"));
        Greetd = new FakeGreetd();
    }

    public string ConfigPath => Path.Combine(_dir.FullName, "greeter.toml");

    public string StatePath => Path.Combine(_dir.FullName, "state", "state.toml");

    public FakeGreetd Greetd { get; }

    public Dictionary<string, string> Environment => new()
    {
        ["BASIN_GREETER_ROOT"] = Path.Combine(_dir.FullName, "root"),
        ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-greeter-tests/session",
        ["DBUS_SYSTEM_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-greeter-tests/system",
        ["BASIN_GREETER_NO_POWER"] = "1",
        ["BASIN_MUTE"] = "1",
        ["BASIN_STILL_CARET"] = "1",
    };

    public string Write(string relative, string text)
    {
        var path = Path.Combine(_dir.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public void Rewrite(string config) =>
        Write("greeter.toml", config + $"\n[sessions]\nenvironments = \"{Path.Combine(_dir.FullName, "environments")}\"\n");

    public string[] Arguments(params string[] extra) =>
        ["--greeter", "--greetd-sock", Greetd.SocketPath, "--state", StatePath, .. extra];

    public EightSession Start(string renderer = "pixman", params string[] extra) =>
        new(renderer, EightSession.Compositor(), ConfigPath, Environment, Arguments(extra));

    public void Dispose()
    {
        Greetd.Dispose();
        _dir.Delete(recursive: true);
    }
}
