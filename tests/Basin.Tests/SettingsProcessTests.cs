using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xunit;

namespace Basin.Tests;

public sealed class SettingsProcessTests
{
    [Fact]
    public void The_panel_opens_over_ipc_applies_live_and_saves_two_lines()
    {
        using var session = SettingsSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or the gl row is not available beside the tests");

        session!.Send("ipc tinycomp/settings {\"state\":\"open\"}");
        Assert.Contains("SETTINGS open=yes", session.WaitForLine("OK tinycomp/settings"));

        session.Send("setting effects.wobbly true");
        Assert.Contains("applied=yes", session.WaitForLine("SETTING "));
        var state = session.WaitForLine("SETTINGS ");
        Assert.Contains(" wobbly=on", state);
        Assert.Contains(" dirty=1", state);

        session.Send("setting canvas.edge_scale 0.15");
        Assert.Contains("applied=yes", session.WaitForLine("SETTING "));
        state = session.WaitForLine("SETTINGS ");
        Assert.Contains(" canvas-edge-scale=0.150", state);
        Assert.Contains(" dirty=2", state);

        var before = File.ReadAllLines(session.ConfigPath);
        session.Send("settings-save");
        Assert.Contains(" dirty=0", session.WaitForLine("SETTINGS "));
        var after = File.ReadAllLines(session.ConfigPath);
        Assert.Equal(before.Length, after.Length);
        var changed = new List<int>();
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                changed.Add(i);
                Assert.Equal(CommentOf(before[i]), CommentOf(after[i]));
            }
        }

        Assert.Equal(2, changed.Count);
        Assert.StartsWith("wobbly   = true", after[changed[0]], StringComparison.Ordinal);
        Assert.StartsWith("edge_scale   = 0.15", after[changed[1]], StringComparison.Ordinal);

        session.Signal("HUP");
        var reload = session.WaitForLine("RELOAD ");
        Assert.Contains(" canvas-edge-scale=0.150", reload);
        session.Send("settings-state");
        state = session.WaitForLine("SETTINGS ");
        Assert.Contains(" dirty=0", state);
        Assert.Contains(" wobbly=on", state);
        Assert.Contains(" status=reloaded-from-file", state);
    }

    [Fact]
    public void A_texture_path_that_does_not_load_shows_in_the_status_and_keeps_the_old_texture()
    {
        using var session = SettingsSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or the gl row is not available beside the tests");

        session!.Send("setting overview.wall \"step\"");
        Assert.Contains("applied=yes", session.WaitForLine("SETTING "));
        session.Send("setting overview.wall_texture \"stone\"");
        Assert.Contains("applied=yes", session.WaitForLine("SETTING "));
        Assert.Contains(" status=none", session.WaitForLine("SETTINGS "));

        session.Send("setting overview.wall_texture \"stnoe\"");
        Assert.NotNull(session.WaitForLine("SETTING "));
        Assert.Contains(" status=wall_texture=error:NOT-FOUND", session.WaitForLine("SETTINGS "));
        session.Send("overview open");
        Assert.Contains(" wall-texture=stone ", session.WaitForLine("OVERVIEW output="));
        session.Send("overview close");

        session.Send("setting overview.wall_texture \"brick\"");
        Assert.NotNull(session.WaitForLine("SETTING "));
        Assert.Contains(" status=none", session.WaitForLine("SETTINGS "));
        session.Send("setting overview.texture_scale 2.0");
        Assert.Contains("applied=yes", session.WaitForLine("SETTING "));
        session.Send("overview open");
        Assert.Contains(" wall-texture=brick ", session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true"));
    }

    [Fact]
    public void The_background_changes_live_on_every_output()
    {
        using var session = SettingsSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or the gl row is not available beside the tests");

        session!.Send("setting compositor.background \"#ff8000\"");
        Assert.Contains("applied=yes", session.WaitForLine("SETTING "));
        var shot = Path.Combine(Path.GetDirectoryName(session.ConfigPath)!, "background.png");
        byte[] pixel = [];
        for (var attempt = 0; attempt < 20; attempt++)
        {
            session.Send($"shotraw {shot}");
            Assert.NotNull(session.WaitForLine("SHOTRAW "));
            var (rgba, width, _) = Basin.Diagnostics.PngCodec.Decode(File.ReadAllBytes(shot));
            var corner = ((5 * width) + 5) * 4;
            pixel = rgba[corner..(corner + 3)];
            if (pixel is [0xff, 0x80, 0x00])
            {
                break;
            }

            Thread.Sleep(100);
        }

        Assert.Equal([0xff, 0x80, 0x00], pixel);
    }

    [Fact]
    public void A_reload_discards_a_change_that_was_never_saved()
    {
        using var session = SettingsSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or the gl row is not available beside the tests");

        session!.Send("setting effects.wobbly true");
        Assert.Contains(" wobbly=on", session.WaitForLine("SETTINGS "));
        session.Send("reload");
        session.WaitForLine("RELOAD ");
        session.Send("settings-state");
        var state = session.WaitForLine("SETTINGS ");
        Assert.Contains(" wobbly=off", state);
        Assert.Contains(" dirty=0", state);
    }

    [Fact]
    public void A_flag_held_key_is_saved_but_the_flag_keeps_the_running_value()
    {
        using var session = SettingsSession.Start(extra: ["--offload", "true"]);
        Assert.SkipWhen(session is null, "tinycomp or the gl row is not available beside the tests");

        session!.Send("setting compositor.offload false");
        Assert.Contains(" offload=on", session.WaitForLine("SETTINGS "));
        session.Send("settings-save");
        Assert.Contains(" offload=on", session.WaitForLine("SETTINGS "));
        Assert.Matches(@"(?m)^offload\s*=\s*false$", File.ReadAllText(session.ConfigPath));
    }

    [Fact]
    public void A_restart_key_is_reported_on_the_next_reload()
    {
        using var session = SettingsSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or the gl row is not available beside the tests");

        session!.Send("setting color.hdr true");
        Assert.Contains("applied=yes", session.WaitForLine("SETTING "));
        session.Send("settings-save");
        session.WaitForLine("SETTINGS ");
        session.Send("reload");
        Assert.Contains("restart-required=color.hdr", session.WaitForLine("RELOAD "));
    }

    [Fact]
    public void A_value_the_parser_refuses_is_not_applied()
    {
        using var session = SettingsSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or the gl row is not available beside the tests");

        session!.Send("setting effects.open \"spin\"");
        var line = session.WaitForLine("SETTING ");
        Assert.Contains("applied=no", line);
        Assert.Contains("error=", line);
    }

    [Fact]
    public void On_pixman_the_panel_is_unavailable_and_says_why()
    {
        using var session = SettingsSession.Start("pixman");
        Assert.SkipWhen(session is null, "tinycomp is not available beside the tests");

        session!.Send("ipc tinycomp/settings {\"state\":\"open\"}");
        var line = session.WaitForLine("ERR ");
        Assert.NotNull(line);
        Assert.Contains("unavailable on pixman", line);
    }

    private static string CommentOf(string line)
    {
        var hash = line.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? string.Empty : line[hash..];
    }

    private sealed class SettingsSession : IDisposable
    {
        private readonly Process _compositor;
        private readonly string _runtimeDir;
        private readonly List<string> _lines = [];
        private readonly object _gate = new();

        private SettingsSession(Process compositor, string runtimeDir)
        {
            _compositor = compositor;
            _runtimeDir = runtimeDir;
        }

        public string ConfigPath => Path.Combine(_runtimeDir, "tinycomp.toml");

        public static SettingsSession? Start(string renderer = "gl", string[]? extra = null, [CallerFilePath] string sourcePath = "")
        {
            if (!OperatingSystem.IsLinux() || Locate("tinycomp") is not { } compositorPath)
            {
                return null;
            }

            if (renderer != "pixman" && !CompositorTestHost.IsRunnable(renderer))
            {
                return null;
            }

            var tag = $"{Environment.ProcessId}-{Environment.TickCount64 % 100000}";
            var runtimeDir = Path.Combine("/tmp", $"basin-settings-{tag}");
            Directory.CreateDirectory(runtimeDir);
            File.SetUnixFileMode(runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var seed = Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "..", "samples", "TinyComp", "tinycomp.toml");
            File.Copy(seed, Path.Combine(runtimeDir, "tinycomp.toml"));

            var info = new ProcessStartInfo(compositorPath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("--backend");
            info.ArgumentList.Add("headless");
            info.ArgumentList.Add("--renderer");
            info.ArgumentList.Add(renderer);
            info.ArgumentList.Add("--config");
            info.ArgumentList.Add(Path.Combine(runtimeDir, "tinycomp.toml"));
            info.ArgumentList.Add("--ipc");
            info.ArgumentList.Add("false");
            foreach (var argument in extra ?? [])
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
            info.Environment.Remove("WAYLAND_DISPLAY");
            var compositor = Process.Start(info)!;
            var session = new SettingsSession(compositor, runtimeDir);
            compositor.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lock (session._gate)
                    {
                        session._lines.Add(line);
                        Monitor.PulseAll(session._gate);
                    }
                }
            };
            compositor.ErrorDataReceived += (_, _) => { };
            compositor.BeginOutputReadLine();
            compositor.BeginErrorReadLine();
            if (session.WaitForLine("SOCKET ") is null)
            {
                session.Dispose();
                return null;
            }

            return session;
        }

        public void Send(string command)
        {
            _compositor.StandardInput.WriteLine(command);
            _compositor.StandardInput.Flush();
        }

        public void Signal(string name)
        {
            using var kill = Process.Start(new ProcessStartInfo("kill", $"-{name} {_compositor.Id}") { UseShellExecute = false })!;
            kill.WaitForExit();
        }

        public string? WaitForLine(string prefix, int timeoutMillis = 15000)
        {
            var deadline = Environment.TickCount64 + timeoutMillis;
            lock (_gate)
            {
                while (true)
                {
                    for (var i = 0; i < _lines.Count; i++)
                    {
                        if (_lines[i].StartsWith(prefix, StringComparison.Ordinal))
                        {
                            var found = _lines[i];
                            _lines.RemoveRange(0, i + 1);
                            return found;
                        }
                    }

                    var remaining = deadline - Environment.TickCount64;
                    if (remaining <= 0 || _compositor.HasExited)
                    {
                        return null;
                    }

                    Monitor.Wait(_gate, (int)Math.Min(remaining, 500));
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_compositor.HasExited)
                {
                    Send("quit");
                    if (!_compositor.WaitForExit(5000))
                    {
                        _compositor.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }

            _compositor.Dispose();
            try
            {
                Directory.Delete(_runtimeDir, recursive: true);
            }
            catch (IOException)
            {
            }
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
    }
}
