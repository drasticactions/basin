using System.Diagnostics;
using System.Globalization;
using Basin.Diagnostics;
using Basin.Tests;
using Xunit;

namespace TinyComp.Tests;

public sealed class WaypipeVsockTests
{
    [Fact]
    public void A_stock_waypipe_server_reaches_basin_headless_over_vsock()
    {
        Assert.SkipWhen(VsockFacts.WaypipeReason() is not null, VsockFacts.WaypipeReason() ?? string.Empty);
        Assert.SkipWhen(VsockFacts.OnPath("weston-simple-shm") is null, "weston-simple-shm is not on PATH");
        var headless = Sample("BasinHeadless", "basinheadless");
        using var runtime = new RuntimeDirectory();
        var port = Port();
        var screenshot = Path.Combine(runtime.Path, "shot.png");

        using var compositor = Start(headless, runtime.Path, "--waypipe-listen", $"vsock:{port}", "--frames", "60", "--screenshot", screenshot);
        Assert.True(compositor.WaitForError("accepting waypipe channels"), $"basinheadless never bound vsock:{port}: {compositor.Errors}");
        using var server = Waypipe(port, runtime.Path);

        Assert.True(compositor.Process.WaitForExit(60_000), "basinheadless never reached its frame count");
        Assert.True(compositor.Process.ExitCode == 0, $"basinheadless exited {compositor.Process.ExitCode}: {compositor.Errors}");
        Assert.Contains("channel 1 attached", compositor.Errors, StringComparison.Ordinal);
        var (rgba, width, height) = PngCodec.Decode(File.ReadAllBytes(screenshot));
        var background = rgba.AsSpan(0, 4).ToArray();
        var painted = 0;
        for (var i = 0; i < width * height * 4; i += 4)
        {
            if (!rgba.AsSpan(i, 4).SequenceEqual(background))
            {
                painted++;
            }
        }

        Assert.True(painted > 10_000, $"the screenshot has {painted} pixels that are not background");
    }

    [Fact]
    public void Two_waypipe_servers_over_vsock_are_two_tinycomp_windows()
    {
        Assert.SkipWhen(VsockFacts.WaypipeReason() is not null, VsockFacts.WaypipeReason() ?? string.Empty);
        Assert.SkipWhen(VsockFacts.OnPath("weston-simple-shm") is null, "weston-simple-shm is not on PATH");
        var tinycomp = Path.Combine(AppContext.BaseDirectory, "tinycomp");
        Assert.SkipUnless(File.Exists(tinycomp), "tinycomp has not been built beside the tests");
        var basinctl = Sample("BasinCtl", "basinctl");
        using var runtime = new RuntimeDirectory();
        var port = Port();

        using var compositor = Start(
            tinycomp, runtime.Path, "--backend", "headless", "--renderer", "pixman", "--config", "false",
            "--waypipe-listen", $"vsock:{port}");
        Assert.True(compositor.WaitForOutput($"CHANNEL vsock:{port}"), $"tinycomp never printed its channel: {compositor.Errors}");
        Assert.True(compositor.WaitForError("accepting waypipe channels"), $"tinycomp never bound vsock:{port}: {compositor.Errors}");
        using var first = Waypipe(port, runtime.Path);
        using var second = Waypipe(port, runtime.Path);

        var socket = Path.Combine(runtime.Path, "basin-wayland-0.sock");
        var deadline = Environment.TickCount64 + 20_000;
        var windows = string.Empty;
        while (Environment.TickCount64 < deadline)
        {
            windows = Run(basinctl, "--socket", socket, "windows");
            if (windows.Split('\n').Count(line => line.Contains("simple-shm", StringComparison.Ordinal)) == 2)
            {
                break;
            }

            Thread.Sleep(200);
        }

        Assert.Equal(2, windows.Split('\n').Count(line => line.Contains("simple-shm", StringComparison.Ordinal)));
        Run(basinctl, "--socket", socket, "quit");
        Assert.True(compositor.Process.WaitForExit(20_000), "tinycomp did not quit");
        Assert.True(first.Process.WaitForExit(10_000), "the first waypipe server outlived the compositor");
        Assert.True(second.Process.WaitForExit(10_000), "the second waypipe server outlived the compositor");
        Assert.True(compositor.WaitForOutput("LIVE 0"), $"tinycomp did not tear down clean: {compositor.Output}");
    }

    private static uint Port() => (uint)Random.Shared.Next(20_000, 60_000);

    private static string Sample(string project, string name)
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var path = Path.Combine(Repository(), "samples", project, "bin", framework.Parent!.Name, framework.Name, name);
        Assert.SkipUnless(File.Exists(path), $"{name} has not been built beside the tests");
        return path;
    }

    private static string Repository()
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && directory is not null; i++)
        {
            if (File.Exists(Path.Combine(directory, "basin.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("basin.slnx is not above the test output");
    }

    private static Child Start(string path, string runtime, params string[] arguments)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["XDG_RUNTIME_DIR"] = runtime;
        info.Environment["BASIN_MUTE"] = "1";
        info.Environment.Remove("WAYLAND_DISPLAY");
        info.Environment.Remove("DISPLAY");
        return new Child(Process.Start(info)!);
    }

    private static Child Waypipe(uint port, string runtime)
    {
        var info = new ProcessStartInfo("waypipe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "--vsock", "-s", $"1:{port.ToString(CultureInfo.InvariantCulture)}", "--compress", "lz4", "--no-gpu",
            "--oneshot", "server", "--", "weston-simple-shm",
        })
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["XDG_RUNTIME_DIR"] = runtime;
        info.Environment.Remove("WAYLAND_DISPLAY");
        return new Child(Process.Start(info)!);
    }

    private static string Run(string path, params string[] arguments)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(20_000), $"{Path.GetFileName(path)} did not exit");
        return output.Result + errors.Result;
    }

    private sealed class Child : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<string> _output = [];
        private readonly List<string> _errors = [];

        public Child(Process process)
        {
            Process = process;
            process.OutputDataReceived += (_, e) => Add(_output, e.Data);
            process.ErrorDataReceived += (_, e) => Add(_errors, e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public Process Process { get; }

        public string Output
        {
            get
            {
                lock (_gate)
                {
                    return string.Join('\n', _output);
                }
            }
        }

        public string Errors
        {
            get
            {
                lock (_gate)
                {
                    return string.Join('\n', _errors);
                }
            }
        }

        public bool WaitForOutput(string text) => Wait(_output, text);

        public bool WaitForError(string text) => Wait(_errors, text);

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(5000);
            }

            Process.Dispose();
        }

        private void Add(List<string> lines, string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                lines.Add(line);
                Monitor.PulseAll(_gate);
            }
        }

        private bool Wait(List<string> lines, string text)
        {
            var deadline = Environment.TickCount64 + 20_000;
            lock (_gate)
            {
                while (!lines.Any(line => line.Contains(text, StringComparison.Ordinal)))
                {
                    var left = deadline - Environment.TickCount64;
                    if (left <= 0)
                    {
                        return false;
                    }

                    Monitor.Wait(_gate, (int)left);
                }

                return true;
            }
        }
    }

    private sealed class RuntimeDirectory : IDisposable
    {
        public RuntimeDirectory()
        {
            Path = System.IO.Path.Combine("/tmp", $"basin-vsock-{Guid.NewGuid():N}"[..24]);
            Directory.CreateDirectory(Path);
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
