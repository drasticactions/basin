using System.Diagnostics;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class TinyCompCanvasProcessTests
{
    private const int ZoneWidth = 154;

    [Fact]
    public void Park_left_draws_the_client_inside_the_zone()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var before = session!.Shot("before");
        Assert.True(Saturated(before, ZoneWidth, before.Width - ZoneWidth) > 0, "the client draws in the centre before the park");

        session.Send("park left");
        session.WaitForLine("PARK ");
        var parked = session.Shot("parked");
        Assert.True(Saturated(parked, 0, ZoneWidth) > 0, "the client draws inside the left zone after the park");
        Assert.Equal(0, Saturated(parked, ZoneWidth, parked.Width - ZoneWidth));
    }

    [Fact]
    public void A_drag_through_the_seam_parks_the_client_in_the_zone()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var (x, y) = session!.WindowPosition();
        session.Send($"move {x + 100} {y + 100}");
        session.Send("key 56 1");
        session.Send("button 272 1");
        for (var cursor = x + 100; cursor > 20; cursor -= 40)
        {
            session.Send($"move {cursor} {y + 100}");
        }

        session.Send("move 10 " + (y + 100));
        session.Send("button 272 0");
        session.Send("key 56 0");
        var dragged = session.Shot("dragged");
        Assert.True(Saturated(dragged, 0, ZoneWidth) > 0, "the client draws inside the left zone after the drag");
        Assert.Equal(0, Saturated(dragged, ZoneWidth, dragged.Width - ZoneWidth));
        var (parkedX, _) = session.WindowPosition();
        Assert.True(parkedX < 0, $"the window's canvas X is {parkedX}");
    }

    [Fact]
    public void Turning_the_canvas_off_recalls_a_parked_window_to_the_centre()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var (homeX, _) = session!.WindowPosition();
        session.Send("park left");
        session.WaitForLine("PARK ");
        _ = session.Shot("parked");
        session.Send("canvas off");
        session.WaitForLine("CANVAS off");
        var line = session.WaitForLine("CANVAS view=0 left=0 right=0", timeoutMillis: 5000);
        Assert.NotNull(line);
        var recalled = session.Shot("recalled");
        Assert.Equal(0, Saturated(recalled, 0, ZoneWidth));
        Assert.True(Saturated(recalled, ZoneWidth, recalled.Width - ZoneWidth) > 0, "the client draws in the centre again");
        Assert.Equal(homeX, session.WindowPosition().X);
    }

    private static int Saturated(Shot shot, int fromX, int width)
    {
        var count = 0;
        for (var y = 0; y < shot.Height; y++)
        {
            for (var x = fromX; x < fromX + width; x++)
            {
                var i = ((y * shot.Width) + x) * 4;
                var r = shot.Rgba[i];
                var g = shot.Rgba[i + 1];
                var b = shot.Rgba[i + 2];
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                if (max - min > 100 && (r > 60 || g > 60))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private readonly record struct Shot(byte[] Rgba, int Width, int Height);

    private sealed class CanvasSession : IDisposable
    {
        private readonly Process _compositor;
        private readonly string _runtimeDir;
        private readonly List<string> _lines = [];
        private readonly object _gate = new();
        private Process? _client;

        private CanvasSession(Process compositor, string runtimeDir)
        {
            _compositor = compositor;
            _runtimeDir = runtimeDir;
        }

        public static CanvasSession? Start()
        {
            if (!OperatingSystem.IsLinux() || Locate("tinycomp") is not { } compositorPath || !ClientAvailable())
            {
                return null;
            }

            var tag = $"{Environment.ProcessId}-{Environment.TickCount64 % 100000}";
            var runtimeDir = Path.Combine("/tmp", $"basin-canvas-{tag}");
            Directory.CreateDirectory(runtimeDir);
            File.SetUnixFileMode(runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var configPath = Path.Combine(runtimeDir, "tinycomp.toml");
            File.WriteAllText(configPath, "[canvas]\nenable = true\ngrid = \"never\"\nanimation_ms = 100\n");

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
            info.ArgumentList.Add("pixman");
            info.ArgumentList.Add("--config");
            info.ArgumentList.Add(configPath);
            info.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
            info.Environment.Remove("WAYLAND_DISPLAY");
            var compositor = Process.Start(info)!;
            var session = new CanvasSession(compositor, runtimeDir);
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

            var socketLine = session.WaitForLine("SOCKET ");
            if (socketLine is null)
            {
                session.Dispose();
                return null;
            }

            var clientInfo = new ProcessStartInfo("weston-simple-shm")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            clientInfo.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
            clientInfo.Environment["WAYLAND_DISPLAY"] = socketLine.Split(' ')[1];
            var client = Process.Start(clientInfo)!;
            client.OutputDataReceived += (_, _) => { };
            client.ErrorDataReceived += (_, _) => { };
            client.BeginOutputReadLine();
            client.BeginErrorReadLine();
            session._client = client;

            if (session.WaitForLine("MAPPED ") is null)
            {
                session.Dispose();
                return null;
            }

            Thread.Sleep(600);
            return session;
        }

        public void Send(string command)
        {
            _compositor.StandardInput.WriteLine(command);
            _compositor.StandardInput.Flush();
            Thread.Sleep(60);
        }

        public string? WaitForLine(string prefix, int timeoutMillis = 15000)
        {
            var deadline = Environment.TickCount64 + timeoutMillis;
            lock (_gate)
            {
                var from = 0;
                while (true)
                {
                    for (; from < _lines.Count; from++)
                    {
                        if (_lines[from].StartsWith(prefix, StringComparison.Ordinal))
                        {
                            var found = _lines[from];
                            _lines.RemoveRange(0, from + 1);
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

        public (int X, int Y) WindowPosition()
        {
            Send("where");
            var line = WaitForLine("WIN ");
            Assert.NotNull(line);
            var parts = line!.Split(' ');
            return (int.Parse(parts[2]), int.Parse(parts[3]));
        }

        public Shot Shot(string name)
        {
            Thread.Sleep(400);
            var path = Path.Combine(_runtimeDir, $"{name}.png");
            Send($"shotraw {path}");
            var line = WaitForLine("SHOTRAW ");
            Assert.True(line == $"SHOTRAW {path}", line ?? "no SHOTRAW line");
            var (rgba, width, height) = PngCodec.Decode(File.ReadAllBytes(path));
            return new Shot(rgba, width, height);
        }

        public void Dispose()
        {
            try
            {
                if (!_compositor.HasExited)
                {
                    _compositor.StandardInput.WriteLine("quit");
                    _compositor.StandardInput.Flush();
                    _ = _compositor.WaitForExit(3000);
                }
            }
            catch (IOException)
            {
            }

            if (_client is { } client && !client.HasExited)
            {
                client.Kill(entireProcessTree: true);
            }

            if (!_compositor.HasExited)
            {
                _compositor.Kill(entireProcessTree: true);
            }

            _compositor.Dispose();
            _client?.Dispose();
            try
            {
                Directory.Delete(_runtimeDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static bool ClientAvailable()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(directory, "weston-simple-shm")))
                {
                    return true;
                }
            }

            return false;
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
