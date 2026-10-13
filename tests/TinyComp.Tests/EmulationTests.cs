using System.Diagnostics;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace TinyComp.Tests;

public sealed class EmulationTests
{
    private const string Config = "[frame]\nstyle = \"flat\"\n\n[overview]\nhot_corner = \"none\"\n";

    internal sealed class Session : IDisposable
    {
        private readonly Process _compositor;
        private readonly string _runtimeDir;
        private readonly List<string> _lines = [];
        private readonly object _gate = new();

        private Session(Process compositor, string runtimeDir, string log)
        {
            _compositor = compositor;
            _runtimeDir = runtimeDir;
            Log = log;
        }

        public string Log { get; }

        public string ConfigPath => Path.Combine(_runtimeDir, "tinycomp.toml");

        public string ClientOutput => File.Exists(Log) ? File.ReadAllText(Log) : string.Empty;

        public static Session? Start(string mode, int seconds, string config = Config, string? command = null, [CallerFilePath] string sourcePath = "")
        {
            var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(sourcePath)));
            var randremu = root is null ? null : Path.Combine(root, "scripts", "wlclients", "bin", "randremu");
            if (!OperatingSystem.IsLinux() || Locate("tinycomp") is not { } compositorPath || randremu is null
                || !File.Exists(randremu) || !OnPath("Xwayland"))
            {
                return null;
            }

            var runtimeDir = Path.Combine("/tmp", $"basin-emu-{Guid.NewGuid().ToString("N")[..12]}");
            Directory.CreateDirectory(runtimeDir);
            File.SetUnixFileMode(runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var configPath = Path.Combine(runtimeDir, "tinycomp.toml");
            File.WriteAllText(configPath, config);
            var log = Path.Combine(runtimeDir, "randremu.txt");

            var info = new ProcessStartInfo(compositorPath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "--backend", "headless", "--renderer", "pixman", "--ipc", "false", "--config", configPath, "-c", command ?? $"exec '{randremu}' {mode} {seconds} > '{log}' 2>&1" })
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
            info.Environment.Remove("WAYLAND_DISPLAY");
            info.Environment.Remove("DISPLAY");
            var compositor = Process.Start(info)!;
            var session = new Session(compositor, runtimeDir, log);
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
            return session;
        }

        public bool WaitForClient(string fragment, int timeoutMillis = 30000)
        {
            var deadline = Environment.TickCount64 + timeoutMillis;
            while (Environment.TickCount64 < deadline)
            {
                if (ClientOutput.Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }

                Thread.Sleep(100);
            }

            return false;
        }

        public string? WaitForLine(string prefix, int timeoutMillis = 15000)
        {
            var deadline = Environment.TickCount64 + timeoutMillis;
            lock (_gate)
            {
                while (true)
                {
                    var index = _lines.FindIndex(line => line.StartsWith(prefix, StringComparison.Ordinal));
                    if (index >= 0)
                    {
                        var found = _lines[index];
                        _lines.RemoveRange(0, index + 1);
                        return found;
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

        public void Send(string command)
        {
            _compositor.StandardInput.WriteLine(command);
            _compositor.StandardInput.Flush();
        }

        public (byte[] Rgba, int Width, int Height) Shot(string name)
        {
            Thread.Sleep(600);
            var path = Path.Combine(_runtimeDir, $"{name}.png");
            Send($"shotraw {path}");
            var line = WaitForLine("SHOTRAW ");
            Assert.True(line is not null, "no SHOTRAW line");
            return PngCodec.Decode(File.ReadAllBytes(path));
        }

        public void Dispose()
        {
            try
            {
                if (!_compositor.HasExited)
                {
                    Send("quit");
                    _ = _compositor.WaitForExit(5000);
                }
            }
            catch (IOException)
            {
            }

            if (!_compositor.HasExited)
            {
                _compositor.Kill(entireProcessTree: true);
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

        private static bool OnPath(string program) =>
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':').Any(dir => File.Exists(Path.Combine(dir, program)));

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

    internal static uint At((byte[] Rgba, int Width, int Height) shot, int x, int y)
    {
        var i = ((y * shot.Width) + x) * 4;
        return (uint)((shot.Rgba[i] << 16) | (shot.Rgba[i + 1] << 8) | shot.Rgba[i + 2]);
    }

    [Fact]
    public void A_four_by_three_mode_is_fitted_with_side_bars()
    {
        using var session = Session.Start("800x600", 30);
        Assert.SkipWhen(session is null, "tinycomp, Xwayland or scripts/wlclients/bin/randremu is not available");

        Assert.True(session!.WaitForClient("CONFIGURE 800 600"), session.ClientOutput);
        Thread.Sleep(800);
        var shot = session.Shot("four-by-three");
        Assert.Equal(1280, shot.Width);
        Assert.Equal(0x000000u, At(shot, 80, 360));
        Assert.Equal(0x000000u, At(shot, 1200, 360));
        Assert.Equal(0xE0E0E0u, At(shot, 170, 10));
        Assert.Equal(0x2050C0u, At(shot, 160 + 96 + 10, 10));
        Assert.Equal(0x2050C0u, At(shot, 170, 96 + 10));
    }

    [Fact]
    public void A_matching_aspect_fills_the_output_and_exit_restores_the_full_size()
    {
        using var session = Session.Start("1024x576", 5);
        Assert.SkipWhen(session is null, "tinycomp, Xwayland or scripts/wlclients/bin/randremu is not available");

        Assert.True(session!.WaitForClient("CONFIGURE 1024 576"), session.ClientOutput);
        Thread.Sleep(800);
        var shot = session.Shot("sixteen-by-nine");
        Assert.Equal(0xE0E0E0u, At(shot, 5, 5));
        Assert.Equal(0x2050C0u, At(shot, 80 + 5, 5));
        Assert.Equal(0x2050C0u, At(shot, 1275, 5));

        Assert.True(session.WaitForClient("RESTORED"), session.ClientOutput);
        Assert.True(session.WaitForClient("CONFIGURE 1280 720", 5000), session.ClientOutput);
    }

    [Fact]
    public void An_emulated_modes_quirk_set_false_keeps_the_full_size()
    {
        var config = Config + "\n[[rule]]\nx11_class = \"randremu\"\nemulated_modes = false\n";
        using var session = Session.Start("800x600", 30, config);
        Assert.SkipWhen(session is null, "tinycomp, Xwayland or scripts/wlclients/bin/randremu is not available");

        Assert.True(session!.WaitForClient("CONFIGURE 1280 720"), session.ClientOutput);
    }
}
