using System.Diagnostics;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class QuillFrameProcessTests
{
    public static TheoryData<string> Rows => ["gl", "skia-gl", "vulkan", "skia-vulkan", "skia-graphite", "impeller"];

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_decorated_window_wears_the_quill_frame(string row)
    {
        using var session = QuillSession.Start(row);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");
        Assert.False(session!.Logged("flat frames are used instead"), $"the {row} row fell back to the flat frames");
        Assert.True(session.Logged("quill frames "), $"the {row} row never reported which device its quill frames draw on");

        var shot = session.Shot("quill");
        var (x, y) = session.WindowPosition();

        var titleY = y - 20;
        Assert.True(titleY >= 0, $"the frame's titlebar is off the top at y={y}");
        var chrome = Distinct(shot, x + 4, titleY, 200);
        Assert.True(chrome > 40, $"the titlebar draws only {chrome} pixels that differ from the desktop");
    }

    [Theory]
    [InlineData("vulkan")]
    [InlineData("skia-vulkan")]
    [InlineData("skia-graphite")]
    public void A_vulkan_compositor_draws_its_quill_frames_on_its_own_vulkan_device(string row)
    {
        using var session = QuillSession.Start(row);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");
        _ = session!.Shot("vulkan");
        Assert.True(
            session.Logged("renderer's VulkanDevice"),
            $"the {row} row did not put its quill frames on the renderer's VulkanDevice");
        Assert.False(session.Logged("GlDevice"), $"the {row} row built a GlDevice for its quill frames");
    }

    [Fact]
    public void A_renderer_that_imports_no_dmabuf_falls_back_rather_than_going_undecorated()
    {
        using var session = QuillSession.Start("pixman");
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        Assert.True(session!.Logged("imports no Argb8888 dmabuf"), "pixman fell back without naming why");

        var shot = session.Shot("fallback");
        var (x, y) = session.WindowPosition();
        var chrome = Distinct(shot, x + 4, y - 20, 200);
        Assert.True(chrome > 40, $"the fallback titlebar draws only {chrome} pixels that differ from the desktop");
    }

    private static int Distinct(Shot shot, int x, int y, int width)
    {
        if (y < 0 || y >= shot.Height)
        {
            return 0;
        }

        var background = Pixel(shot, 5, 5);
        var count = 0;
        for (var offset = 0; offset < width && x + offset < shot.Width; offset++)
        {
            if (Pixel(shot, x + offset, y) != background)
            {
                count++;
            }
        }

        return count;
    }

    private static uint Pixel(Shot shot, int x, int y)
    {
        var i = ((y * shot.Width) + x) * 4;
        return (uint)((shot.Rgba[i] << 16) | (shot.Rgba[i + 1] << 8) | shot.Rgba[i + 2]);
    }

    private sealed record Shot(byte[] Rgba, int Width, int Height);

    private sealed class QuillSession : IDisposable
    {
        private readonly Process _compositor;
        private readonly string _runtimeDir;
        private readonly List<string> _lines = [];
        private readonly List<string> _errors = [];
        private readonly object _gate = new();
        private Process? _client;

        private QuillSession(Process compositor, string runtimeDir)
        {
            _compositor = compositor;
            _runtimeDir = runtimeDir;
        }

        public static QuillSession? Start(string renderer = "gl", [CallerFilePath] string sourcePath = "")
        {
            if (!OperatingSystem.IsLinux() || Locate("tinycomp") is not { } compositorPath ||
                ClientPath(sourcePath) is not { } clientPath)
            {
                return null;
            }

            if (renderer != "pixman" && !CompositorTestHost.IsRunnable(renderer))
            {
                return null;
            }

            var tag = $"{Environment.ProcessId}-{Environment.TickCount64 % 100000}";
            var runtimeDir = Path.Combine("/tmp", $"basin-quill-{tag}");
            Directory.CreateDirectory(runtimeDir);
            File.SetUnixFileMode(runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var configPath = Path.Combine(runtimeDir, "tinycomp.toml");
            File.WriteAllText(configPath, "[frame]\nstyle = \"quill\"\n\n[frame.quill]\ncorner_radius = 8\n");

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
            info.ArgumentList.Add(configPath);
            info.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
            info.Environment.Remove("WAYLAND_DISPLAY");
            var compositor = Process.Start(info)!;
            var session = new QuillSession(compositor, runtimeDir);
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
            compositor.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lock (session._gate)
                    {
                        session._errors.Add(line);
                    }
                }
            };
            compositor.BeginOutputReadLine();
            compositor.BeginErrorReadLine();

            var socketLine = session.WaitForLine("SOCKET ");
            if (socketLine is null)
            {
                session.Dispose();
                return null;
            }

            var clientInfo = new ProcessStartInfo(clientPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            clientInfo.ArgumentList.Add("server");
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

            Thread.Sleep(800);
            return session;
        }

        public bool Logged(string fragment)
        {
            lock (_gate)
            {
                return _errors.Exists(line => line.Contains(fragment, StringComparison.Ordinal));
            }
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
            Assert.True(line is not null && line.StartsWith($"SHOTRAW {path}", StringComparison.Ordinal), line ?? "no SHOTRAW line");
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

        private static string? ClientPath(string sourcePath)
        {
            var tests = Path.GetDirectoryName(sourcePath);
            var root = tests is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(tests));
            if (root is null)
            {
                return null;
            }

            var candidate = Path.Combine(root, "scripts", "wlclients", "bin", "ssdwin");
            return File.Exists(candidate) ? candidate : null;
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
