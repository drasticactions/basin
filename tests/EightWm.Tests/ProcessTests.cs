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
        await session.SendAsync("charms on");
        await session.WaitForAsync(line => line == "CHARMS on", fresh: true);
        await session.SendAsync("touch 0.967 0.25");
        Assert.NotNull(await session.WaitForAsync(line => line == "CHARM Search"));
        await session.SendAsync("touch 0.1 0.4");
        Assert.NotNull(await session.WaitForAsync(line => line == "PANE off Search"));
        Assert.Null(await session.WaitForAsync(line => line.StartsWith("LAUNCH ", StringComparison.Ordinal), attempts: 5));

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
        await session.SendAsync("touchdrag 0.1 0.3 0 -0.08");
        Assert.NotNull(await session.WaitForAsync(line => line == "SELECT Simple SHM off"));
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

    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private readonly List<string> _lines = [];
        private readonly Lock _gate = new();

        public Session(string path, string config, IReadOnlyDictionary<string, string>? environment = null)
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "--backend", "headless", "--renderer", "pixman", "--xwayland", "off", "--config", config })
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
                    lock (_gate)
                    {
                        _lines.Add(e.Data);
                    }
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

            for (var i = 0; i < attempts; i++)
            {
                if (poke is not null)
                {
                    await SendAsync(poke);
                }

                lock (_gate)
                {
                    for (var j = _lines.Count - 1; j >= floor; j--)
                    {
                        if (predicate(_lines[j]))
                        {
                            return _lines[j];
                        }
                    }
                }

                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            return null;
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
