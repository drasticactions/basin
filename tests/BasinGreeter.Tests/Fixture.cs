using Basin.Tests;

namespace BasinGreeter.Tests;

internal sealed class Fixture : IDisposable
{
    private readonly TempDir _dir = new();

    public Fixture(string? config = null, string sessions = "")
    {
        _dir.Write("root/etc/passwd", string.Join('\n',
            "root:x:0:0:root:/root:/bin/bash",
            "ada:x:1000:1000:Ada Lovelace:/home/ada:/bin/bash",
            "charles:x:1001:1001:Charles Babbage,,,:/home/charles:/bin/zsh",
            "nobody:x:65534:65534:Nobody:/:/usr/bin/nologin",
            ""));
        _dir.Write("root/etc/login.defs", "UID_MIN 1000\nUID_MAX 60000\n");
        _dir.Write("root/usr/share/wayland-sessions/sway.desktop", "[Desktop Entry]\nType=Application\nName=Sway\nExec=sway\nDesktopNames=sway;wlroots;\n");
        _dir.Write("root/usr/share/wayland-sessions/tinycomp.desktop", "[Desktop Entry]\nType=Application\nName=TinyComp\nExec=tinycomp --config false\n");
        _dir.Write("root/usr/share/xsessions/i3.desktop", "[Desktop Entry]\nType=Application\nName=i3\nExec=i3\nDesktopNames=i3\n");
        ConfigPath = _dir.Write("basin-greeter.toml", (config ?? "") + $"\n[sessions]\nenvironments = \"{System.IO.Path.Combine(_dir.Path, "environments")}\"\n{sessions}\n");
        Greetd = new FakeGreetd();
    }

    public string Root => System.IO.Path.Combine(_dir.Path, "root");

    public string ConfigPath { get; }

    public FakeGreetd Greetd { get; }

    public string Write(string name, string text) => _dir.Write(name, text);

    public Dictionary<string, string?> Environment => new()
    {
        ["BASIN_GREETER_ROOT"] = Root,
    };

    public string StatePath => System.IO.Path.Combine(_dir.Path, "state", "state.toml");

    public GreeterSession Start(string renderer = "pixman", params string[] extra)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StatePath)!);
        return new(GreeterSession.Headless(renderer, ConfigPath, ["--greetd-sock", Greetd.SocketPath, "--state", StatePath, .. extra]), Environment);
    }

    public void Rewrite(string config, string sessions = "") =>
        File.WriteAllText(ConfigPath, config + $"\n[sessions]\nenvironments = \"{System.IO.Path.Combine(_dir.Path, "environments")}\"\n{sessions}\n");

    public void Dispose()
    {
        Greetd.Dispose();
        _dir.Dispose();
    }
}
