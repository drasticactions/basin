using System.Diagnostics;
using Xunit;

namespace EightWm.Tests;

public sealed class ProcessTests
{
    private const string Config = """
        [start]
        scan_desktop_files = false

        [[tile]]
        name = "Simple SHM"
        exec = "weston-simple-shm"
        size = "wide"
        color = "#2d89ef"

        [[tile]]
        name = "Flower"
        exec = "true"
        color = "#00aba9"

        [[tile]]
        name = "Terminal"
        exec = "true"
        color = "#603cba"
        group = "More"

        [[tile]]
        name = "Clock"
        exec = "true"
        size = "small"
        color = "#da532c"
        group = "More"
        """;

    private static bool Realized(string line) =>
        line.StartsWith("TILE 0 ", StringComparison.Ordinal) && !line.EndsWith("box=-", StringComparison.Ordinal);

    private static (string Compositor, string Client)? Prerequisites()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("eight-wm");
        Assert.SkipWhen(compositor is null, "eight-wm has not been built beside the tests");
        var client = Which("weston-simple-shm");
        Assert.SkipWhen(client is null, "weston-simple-shm is not installed");
        return (compositor!, client!);
    }

    [Fact]
    public async Task A_launch_rule_starts_an_X11_program_on_the_low_density_server()
    {
        var (compositor, _) = Prerequisites()!.Value;
        Assert.SkipUnless(Which("Xwayland") is not null && Which("xprop") is not null, "Xwayland and xprop are needed");
        using var config = new TempFile("[[launch]]\nexec = \"xprop\"\nx11 = \"low-density\"\n");
        var output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"eight-low-density-{Guid.NewGuid():N}.txt");
        try
        {
            using var session = new Session(compositor, config.Path, null, "--xwayland", "on", "--scale", "2");
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SendAsync($"launch xprop -root RESOURCE_MANAGER > '{output}'");
            var deadline = Environment.TickCount64 + 30000;
            while (Environment.TickCount64 < deadline && !(File.Exists(output) && File.ReadAllText(output).Contains("Xft.dpi", StringComparison.Ordinal)))
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            Assert.Contains("Xft.dpi:\\t96", File.Exists(output) ? File.ReadAllText(output) : "no output", StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task Start_tiles_launch_and_the_chrome_answers_the_stdin_commands()
    {
        var (compositor, _) = Prerequisites()!.Value;
        using var config = new TempFile(Config);
        using var session = new Session(compositor, config.Path);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));

        var tile = await session.WaitForAsync(Realized, poke: "tiles");
        Assert.NotNull(tile);
        Assert.Contains("name=Simple SHM", tile!, StringComparison.Ordinal);
        Assert.Contains("box=65,187,310x150", tile, StringComparison.Ordinal);
        Assert.Contains("box=455,347,70x70", await session.WaitForAsync(line => line.StartsWith("TILE 3 ", StringComparison.Ordinal)), StringComparison.Ordinal);

        await session.SendAsync("select 1");
        Assert.NotNull(await session.WaitForAsync(line => line == "SELECT Flower on"));
        await session.SendAsync("press 2 0.9 0.1");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("PRESS Terminal", StringComparison.Ordinal)));
        await session.SendAsync("release");
        Assert.NotNull(await session.WaitForAsync(line => line == "RELEASE"));

        await session.SendAsync("zoom out");
        Assert.NotNull(await session.WaitForAsync(line => line == "ZOOM out"));
        await session.SendAsync("zoom in");
        Assert.NotNull(await session.WaitForAsync(line => line == "ZOOM in"));
        await session.SendAsync("apps on");
        Assert.NotNull(await session.WaitForAsync(line => line == "APPS on"));
        await session.SendAsync("apps off");
        Assert.NotNull(await session.WaitForAsync(line => line == "APPS off"));

        await session.SendAsync("charms on");
        Assert.NotNull(await session.WaitForAsync(line => line == "CHARMS on"));
        await session.SendAsync("charm settings");
        Assert.NotNull(await session.WaitForAsync(line => line == "CHARM Settings"));
        var pane = await session.WaitForAsync(line => line.StartsWith("  charms ", StringComparison.Ordinal), poke: "chrome", fresh: true);
        Assert.Contains("pane=open", pane!, StringComparison.Ordinal);
        Assert.Contains("panebox=1021,0,345x768", pane, StringComparison.Ordinal);
        await session.SendAsync("key Escape");
        Assert.NotNull(await session.WaitForAsync(line => line == "PANE off Settings"));

        await session.SendAsync("touch 0.967 0.25");
        Assert.Null(await session.WaitForAsync(line => line.StartsWith("CHARM ", StringComparison.Ordinal) && line != "CHARM Settings", attempts: 5));
        var charms = session.Mark();
        await session.SendAsync("charms on");
        Assert.NotNull(await session.WaitForAsync(line => line == "CHARMS on", since: charms));
        await session.SettleAsync();
        await session.SendAsync("touch 0.967 0.25");
        Assert.NotNull(await session.WaitForAsync(line => line == "CHARM Search"));
        await session.SendAsync("touch 0.1 0.4");
        Assert.NotNull(await session.WaitForAsync(line => line == "PANE off Search"));
        await session.SettleAsync();
        Assert.Null(await session.WaitForAsync(line => line.StartsWith("LAUNCH ", StringComparison.Ordinal), attempts: 1));

        await session.SendAsync("touch 0.1 0.4");
        Assert.NotNull(await session.WaitForAsync(line => line == "LAUNCH Simple SHM"));
        var app = await session.WaitForAsync(line => line.StartsWith("APP + ", StringComparison.Ordinal));
        Assert.NotNull(app);
        var chrome = await session.WaitForAsync(line => line.StartsWith("CHROME ", StringComparison.Ordinal) && line.Contains("splash=closed", StringComparison.Ordinal), poke: "chrome", fresh: true);
        Assert.NotNull(chrome);

        await session.SendAsync("title on");
        Assert.NotNull(await session.WaitForAsync(line => line == "TITLE on"));
        await session.SendAsync("titledrag 0.1 0.5");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TITLE drop Left", StringComparison.Ordinal)));

        var scene = await session.WaitForAsync(line => line.StartsWith("SCENE surfaces=", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.Equal("SCENE surfaces=1", scene);

        await session.SendAsync("close");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP - ", StringComparison.Ordinal)));
        await session.SendAsync("quit");
        var frames = await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
        Assert.NotNull(frames);
        var fields = frames!.Split(' ');
        Assert.Equal("LIVE", fields[2]);
        if (fields[3] != "untracked")
        {
            Assert.Equal("0", fields[3]);
        }
    }

    [Fact]
    public async Task A_touch_swipe_on_a_tile_selects_and_a_long_one_reorders()
    {
        var (compositor, _) = Prerequisites()!.Value;
        using var config = new TempFile(Config);
        using var session = new Session(compositor, config.Path);
        Assert.NotNull(await session.WaitForAsync(Realized, poke: "tiles"));

        await session.SendAsync("touchdrag 0.1 0.3 0 0.08");
        Assert.NotNull(await session.WaitForAsync(line => line == "SELECT Simple SHM on"));
        await session.SettleAsync();
        await session.SendAsync("touchdrag 0.1 0.3 0 -0.08");
        Assert.NotNull(await session.WaitForAsync(line => line == "SELECT Simple SHM off"));
        await session.SettleAsync();
        await session.SendAsync("touchdrag 0.1 0.35 0.02 0.25");
        Assert.NotNull(await session.WaitForAsync(line => line == "REORDER Simple SHM 0->1"));
        var moved = await session.WaitForAsync(line => line.StartsWith("TILE 1 ", StringComparison.Ordinal), poke: "tiles", fresh: true);
        Assert.Contains("name=Simple SHM", moved!, StringComparison.Ordinal);

        await session.SendAsync("touchdrag 0.8 0.7 0 -0.3");
        Assert.NotNull(await session.WaitForAsync(line => line == "APPS on"));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_idle_start_screen_does_not_repaint()
    {
        var (compositor, _) = Prerequisites()!.Value;
        using var config = new TempFile(Config);
        using var session = new Session(compositor, config.Path);
        Assert.NotNull(await session.WaitForAsync(Realized, poke: "tiles"));

        await Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);
        await session.SendAsync("quit");
        var frames = await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
        Assert.NotNull(frames);
        Assert.InRange(int.Parse(frames!.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture), 1, 6);
    }

    [Fact]
    public async Task A_tile_flips_into_its_app_and_a_second_tap_reveals_the_running_one()
    {
        var (compositor, _) = Prerequisites()!.Value;
        using var config = new TempFile(Config);
        var data = Directory.CreateTempSubdirectory("eight-wm-tests-");
        try
        {
            Directory.CreateDirectory(Path.Combine(data.FullName, "applications"));
            File.WriteAllText(
                Path.Combine(data.FullName, "applications", "simple-shm.desktop"),
                "[Desktop Entry]\nType=Application\nName=Simple SHM\nExec=weston-simple-shm\n");
            using var session = new Session(
                compositor,
                config.Path,
                new Dictionary<string, string> { ["XDG_DATA_HOME"] = data.FullName, ["XDG_DATA_DIRS"] = data.FullName });
            Assert.NotNull(await session.WaitForAsync(Realized, poke: "tiles"));

            await session.SendAsync("tap 0");
            Assert.NotNull(await session.WaitForAsync(line => line == "FLIP begin Simple SHM from=65,187,310x150 to=0,0,1366x768"));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ", StringComparison.Ordinal)));
            Assert.NotNull(await session.WaitForAsync(line => line == "FLIP end Simple SHM"));

            await session.SendAsync("start");
            Assert.NotNull(await session.WaitForAsync(line => line == "START on"));
            var mark = session.Mark();
            await session.SendAsync("tap 0");
            Assert.NotNull(await session.WaitForAsync(line => line == "LAUNCH Simple SHM running=org.freedesktop.weston.simple-shm"));
            Assert.NotNull(await session.WaitForAsync(line => line == "FLIP back Simple SHM app", since: mark));
            Assert.NotNull(await session.WaitForAsync(line => line == "FLIP end Simple SHM", since: mark));
            var scene = await session.WaitForAsync(line => line.StartsWith("SCENE surfaces=", StringComparison.Ordinal), poke: "where", fresh: true);
            Assert.Equal("SCENE surfaces=1", scene);

            await session.SendAsync("quit");
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_splitter_dragged_to_the_edge_gives_the_app_the_whole_screen()
    {
        var (compositor, _) = Prerequisites()!.Value;
        using var config = new TempFile(Config);
        using var session = new Session(compositor, config.Path);
        Assert.NotNull(await session.WaitForAsync(Realized, poke: "tiles"));

        await session.SendAsync("tap 0");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ", StringComparison.Ordinal)));
        await session.SendAsync("snap 0 left");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SNAP ", StringComparison.Ordinal)));
        await session.SendAsync("title on");
        Assert.NotNull(await session.WaitForAsync(line => line == "TITLE on"));

        await session.SendAsync("touchdrag 0.5 0.5 0.1 0");
        var resized = await session.WaitForAsync(line => line.StartsWith("CELLS ", StringComparison.Ordinal), poke: "cells", fresh: true);
        Assert.Contains("vacant=", resized!, StringComparison.Ordinal);
        var width = System.Text.RegularExpressions.Regex.Match(resized!, @"widths=\[(\d+),").Groups[1].Value;
        var narrowed = await session.WaitForAsync(line => line.StartsWith("  title ", StringComparison.Ordinal), poke: "chrome", fresh: true);
        Assert.Contains($"box=0,0,{width}x48", narrowed!, StringComparison.Ordinal);

        await session.SendAsync("touchdrag 0.6 0.5 0.38 0");
        Assert.NotNull(await session.WaitForAsync(line => line == "COLLAPSE vacancy"));
        var cells = await session.WaitForAsync(line => line.StartsWith("CELLS ", StringComparison.Ordinal), poke: "cells", fresh: true);
        Assert.Contains("widths=[1366] ", cells!, StringComparison.Ordinal);
        Assert.DoesNotContain("vacant=", cells, StringComparison.Ordinal);
        var title = await session.WaitForAsync(line => line.StartsWith("  title ", StringComparison.Ordinal), poke: "chrome", fresh: true);
        Assert.Contains("box=0,0,1366x48", title!, StringComparison.Ordinal);

        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_drag_from_the_top_edge_picks_the_app_up()
    {
        var (compositor, _) = Prerequisites()!.Value;
        using var config = new TempFile(Config);
        using var session = new Session(compositor, config.Path);
        Assert.NotNull(await session.WaitForAsync(Realized, poke: "tiles"));

        await session.SendAsync("tap 0");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line == "FLIP end Simple SHM"));

        var mark = session.Mark();
        await session.SendAsync("touchdrag 0.5 0.001 0 0.45");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TITLE lift ", StringComparison.Ordinal), since: mark));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TITLE drop Middle ", StringComparison.Ordinal), since: mark));

        mark = session.Mark();
        await session.SendAsync("launch weston-simple-shm");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ", StringComparison.Ordinal), since: mark));

        mark = session.Mark();
        await session.SendAsync("touchdrag 0.4 0.001 -0.3 0.65");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TITLE drop Left ", StringComparison.Ordinal), since: mark));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SNAP fill ", StringComparison.Ordinal), since: mark));
        var cells = await session.WaitForAsync(line => line.StartsWith("CELLS ", StringComparison.Ordinal), poke: "cells", fresh: true);
        Assert.Contains("widths=[672,672]", cells!, StringComparison.Ordinal);
        Assert.DoesNotContain("vacant=", cells, StringComparison.Ordinal);

        mark = session.Mark();
        await session.SendAsync("touchdrag 0.3 0.001 0 0.97");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TITLE drop Bottom ", StringComparison.Ordinal), since: mark));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP - ", StringComparison.Ordinal), since: mark));

        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_session_lock_takes_the_keyboard_from_the_app_until_it_unlocks()
    {
        var (compositor, _) = Prerequisites()!.Value;
        var ssdwin = Repository("scripts/wlclients/bin/ssdwin");
        Assert.SkipWhen(ssdwin is null, "scripts/wlclients/bin/ssdwin has not been built");
        var script = Repository("scripts/session-lock.sh");
        Assert.SkipWhen(script is null, "scripts/session-lock.sh is missing");
        using var config = new TempFile(Config);
        using var session = new Session(compositor, config.Path);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);
        var display = socket!.Split(' ')[1];

        await session.SendAsync("key a");
        Assert.NotNull(await session.WaitForAsync(line => line == "KEY a"));
        var keys = new List<string>();
        using var app = StartListening(ssdwin!, display, keys, "server");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("key a");
        Assert.True(await WaitForLineAsync(keys, "KEY 30 1"), "the app did not get the key before the lock");

        using (var locker = StartListening(script!, display, [], "2", "FF227744"))
        {
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCK on", poke: "where", fresh: true));
            await session.SendAsync("key b");
            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCKED"));
            lock (keys)
            {
                Assert.DoesNotContain(keys, line => line.StartsWith("KEY 48 ", StringComparison.Ordinal));
            }
        }

        Assert.NotNull(await session.WaitForAsync(line => line == "LOCK off", poke: "where", fresh: true));
        await session.SendAsync("key c");
        Assert.True(await WaitForLineAsync(keys, "KEY 46 1"), "the keyboard did not go back to the app after the unlock");
        app.Kill(entireProcessTree: true);

        await session.SendAsync("quit");
        var frames = await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
        Assert.NotNull(frames);
        var live = frames!.Split(' ');
        Assert.Equal("LIVE", live[2]);
        if (live[3] != "untracked")
        {
            Assert.Equal("0", live[3]);
        }
    }

    [Fact]
    public async Task A_keyboard_layer_surface_takes_the_keyboard_and_gives_it_back_when_it_unmaps()
    {
        var (compositor, _) = Prerequisites()!.Value;
        var ssdwin = Repository("scripts/wlclients/bin/ssdwin");
        Assert.SkipWhen(ssdwin is null, "scripts/wlclients/bin/ssdwin has not been built");
        var panel = Repository("scripts/wlclients/bin/panel");
        Assert.SkipWhen(panel is null, "scripts/wlclients/bin/panel has not been built");
        using var config = new TempFile(Config);
        using var session = new Session(compositor, config.Path);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);
        var display = socket!.Split(' ')[1];

        await session.SendAsync("key a");
        Assert.NotNull(await session.WaitForAsync(line => line == "KEY a"));
        var keys = new List<string>();
        using var app = StartListening(ssdwin!, display, keys, "server");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("key a");
        Assert.True(await WaitForLineAsync(keys, "KEY 30 1"), "the app did not get the key before the panel mapped");

        var output = new List<string>();
        using (var layer = StartListening(panel!, display, output, "top", "32", "keyboard"))
        {
            Assert.True(await WaitForLineAsync(output, "PANEL 1366x32"), "the panel was never configured");
            Assert.NotNull(await session.WaitForAsync(line => line == "LAYER + panel Top"));
            Assert.True(await WaitForLineAsync(output, "KEYBOARD enter"), "the panel did not get the keyboard");
            await session.SendAsync("key b");
            Assert.True(await WaitForLineAsync(output, "KEY 48 1"), "the key did not reach the panel");
            lock (keys)
            {
                Assert.DoesNotContain(keys, line => line.StartsWith("KEY 48 ", StringComparison.Ordinal));
            }

            layer.Kill(entireProcessTree: true);
        }

        Assert.NotNull(await session.WaitForAsync(line => line == "LAYER - panel"));
        await session.SendAsync("key c");
        Assert.True(await WaitForLineAsync(keys, "KEY 46 1"), "the keyboard did not go back to the app after the panel left");
        app.Kill(entireProcessTree: true);

        await session.SendAsync("quit");
        var frames = await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
        Assert.NotNull(frames);
        var live = frames!.Split(' ');
        Assert.Equal("LIVE", live[2]);
        if (live[3] != "untracked")
        {
            Assert.Equal("0", live[3]);
        }
    }

    private static Process StartListening(string path, string display, List<string> lines, params string[] arguments)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["WAYLAND_DISPLAY"] = display;
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (lines)
                {
                    lines.Add(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<bool> WaitForLineAsync(List<string> lines, string exact)
    {
        for (var i = 0; i < 500; i++)
        {
            lock (lines)
            {
                if (lines.Contains(exact))
                {
                    return true;
                }
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static string? Repository(string relative)
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static string? Which(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':'))
        {
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name)))
            {
                return Path.Combine(directory, name);
            }
        }

        return null;
    }

    private static string? Locate(string name)
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string contents)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"eight-wm-tests-{Environment.ProcessId}-{Guid.NewGuid():N}.toml");
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    [Fact]
    public async Task Super_alt_8_zooms_and_super_alt_c_draws_the_screen_curtain()
    {
        var (compositor, _) = Prerequisites()!.Value;
        using var config = new TempFile(Config);
        using var session = new Session(compositor, config.Path);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        await session.SendAsync("key Super+Alt+8");
        Assert.NotNull(await session.WaitForAsync(line => line == "ZOOM factor=2"));
        await session.SendAsync("key Super+Alt+c");
        Assert.NotNull(await session.WaitForAsync(line => line == "CURTAIN on"));
        await session.SendAsync("key Super+Alt+c");
        Assert.NotNull(await session.WaitForAsync(line => line == "CURTAIN off"));
        await session.SendAsync("key Super+Alt+minus");
        Assert.NotNull(await session.WaitForAsync(line => line == "ZOOM factor=1.6"));
    }

    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private readonly List<string> _lines = [];
        private readonly Lock _gate = new();
        private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Session(string path, string config, IReadOnlyDictionary<string, string>? environment = null, params string[] extra)
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            string[] arguments = extra.Length > 0
                ? ["--backend", "headless", "--renderer", "pixman", "--config", config, .. extra]
                : ["--backend", "headless", "--renderer", "pixman", "--xwayland", "off", "--config", config];
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            {
                info.Environment[name] = value;
            }

            _process = Process.Start(info)!;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    TaskCompletionSource arrived;
                    lock (_gate)
                    {
                        _lines.Add(e.Data);
                        arrived = _arrived;
                        _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }

                    arrived.TrySetResult();
                }
            };
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public async Task SendAsync(string command)
        {
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();
        }

        public int Mark()
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }

        public async Task SettleAsync()
        {
            var mark = Mark();
            await SendAsync("settle");
            var reply = await WaitForAsync(
                line => line.StartsWith("OK session/settle ", StringComparison.Ordinal) || line.StartsWith("ERR ", StringComparison.Ordinal),
                since: mark);
            Assert.True(reply is not null && reply.StartsWith("OK ", StringComparison.Ordinal), $"settle answered [{reply}]");
        }

        public async Task<string?> WaitForAsync(
            Func<string, bool> predicate,
            string? poke = null,
            bool fresh = false,
            int attempts = 100,
            int since = 0)
        {
            var floor = since;
            if (fresh)
            {
                lock (_gate)
                {
                    floor = _lines.Count;
                }
            }

            var deadline = Environment.TickCount64 + (attempts * 100L);
            var cancel = TestContext.Current.CancellationToken;
            while (true)
            {
                if (poke is not null)
                {
                    await SendAsync(poke);
                }

                var slotEnd = poke is null ? deadline : Math.Min(deadline, Environment.TickCount64 + 100);
                while (true)
                {
                    Task next;
                    lock (_gate)
                    {
                        for (var j = _lines.Count - 1; j >= floor; j--)
                        {
                            if (predicate(_lines[j]))
                            {
                                return _lines[j];
                            }
                        }

                        next = _arrived.Task;
                    }

                    var remaining = slotEnd - Environment.TickCount64;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    await Task.WhenAny(next, Task.Delay((int)remaining, cancel));
                    cancel.ThrowIfCancellationRequested();
                }

                if (Environment.TickCount64 >= deadline)
                {
                    return null;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }

                _process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
            }

            _process.Dispose();
        }
    }
}
