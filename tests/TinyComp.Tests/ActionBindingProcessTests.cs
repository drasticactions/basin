using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace TinyComp.Tests;

public sealed class ActionBindingProcessTests
{
    private sealed class Run : IDisposable
    {
        private readonly List<Process> _children = [];
        private readonly string _runtimeDir;

        public Run()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "linux only");
            var tinycomp = Locate("tinycomp");
            Assert.SkipWhen(tinycomp is null, "the tinycomp binary is not built");
            Assert.SkipWhen(Client("actionbind") is null || Client("ssdwin") is null, "scripts/wlclients is not built");
            _runtimeDir = Path.Combine("/tmp", $"basin-bind-{Guid.NewGuid():N}"[..24]);
            Directory.CreateDirectory(_runtimeDir);
            File.SetUnixFileMode(_runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var config = Path.Combine(_runtimeDir, "tinycomp.toml");
            File.WriteAllText(config, "[overview]\nhot_corner = \"none\"\n");
            Compositor = Start(tinycomp!, ["--backend", "headless", "--renderer", "pixman", "--config", config], redirectInput: true);
            var clock = Stopwatch.StartNew();
            while (Socket is null && clock.ElapsedMilliseconds < 20_000)
            {
                Socket = Lines(Compositor).FirstOrDefault(line => line.StartsWith("SOCKET ", StringComparison.Ordinal))?[7..];
                Thread.Sleep(50);
            }

            Assert.NotNull(Socket);
        }

        public Process Compositor { get; }

        public string? Socket { get; }

        public Process Spawn(string name, params string[] arguments)
        {
            var process = Start(Client(name)!, arguments, redirectInput: false);
            _children.Add(process);
            return process;
        }

        public void Key(uint code, bool pressed)
        {
            Compositor.StandardInput.WriteLine($"key {code} {(pressed ? 1 : 0)}");
            Compositor.StandardInput.Flush();
            Thread.Sleep(60);
        }

        public void Tap(params uint[] codes)
        {
            foreach (var code in codes)
            {
                Key(code, true);
            }

            for (var i = codes.Length - 1; i >= 0; i--)
            {
                Key(codes[i], false);
            }
        }

        public static bool WaitFor(Process process, Func<List<string>, bool> condition, int ms = 5000)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms)
            {
                if (condition(Lines(process)))
                {
                    return true;
                }

                Thread.Sleep(50);
            }

            return condition(Lines(process));
        }

        public static List<string> Lines(Process process) => [.. Output[process]];

        public void Dispose()
        {
            foreach (var child in _children)
            {
                Stop(child);
            }

            Stop(Compositor);
            try
            {
                Directory.Delete(_runtimeDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static readonly ConcurrentDictionary<Process, ConcurrentQueue<string>> Output = new();

        private Process Start(string path, string[] arguments, bool redirectInput)
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["XDG_RUNTIME_DIR"] = _runtimeDir;
            info.Environment["BASIN_MUTE"] = "1";
            if (Socket is { } socket)
            {
                info.Environment["WAYLAND_DISPLAY"] = socket;
            }
            else
            {
                info.Environment.Remove("WAYLAND_DISPLAY");
            }

            var process = Process.Start(info)!;
            var lines = new ConcurrentQueue<string>();
            Output[process] = lines;
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lines.Enqueue(line);
                }
            };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private static void Stop(Process process)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }

            Output.TryRemove(process, out _);
            process.Dispose();
        }

        private static string? Client(string name)
        {
            var root = Locate("basin.slnx");
            var path = root is null ? null : Path.Combine(Path.GetDirectoryName(root)!, "scripts", "wlclients", "bin", name);
            return path is not null && File.Exists(path) ? path : null;
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

    private const uint Ctrl = 29;
    private const uint Shift = 42;
    private const uint F9 = 67;

    [Theory]
    [InlineData("no_block", 1, true)]
    [InlineData("block", 1, false)]
    [InlineData("one_shot", 1, false)]
    public void An_action_binding_fires_on_its_chord_and_honors_its_flags(string mode, int triggers, bool clientSeesKey)
    {
        using var run = new Run();
        var window = run.Spawn("ssdwin");
        Assert.True(Run.WaitFor(window, lines => lines.Any(line => line.StartsWith("CONFIGURE", StringComparison.Ordinal) || line.StartsWith("DECORATION", StringComparison.Ordinal))));
        Thread.Sleep(500);
        string[] arguments = mode == "block" ? ["Control_L", "Shift_L", "F9", "10"] : ["Control_L", "Shift_L", "F9", mode, "10"];
        var binding = run.Spawn("actionbind", arguments);
        Assert.True(Run.WaitFor(binding, lines => lines.Any(line => line.StartsWith("READY", StringComparison.Ordinal))));

        run.Tap(Ctrl, Shift, F9);
        run.Tap(Ctrl, Shift, F9);
        Thread.Sleep(500);

        var expectedTriggers = mode == "one_shot" ? 1 : 2 * triggers;
        Assert.Equal(expectedTriggers, Run.Lines(binding).Count(line => line.StartsWith("TRIGGERED", StringComparison.Ordinal)));
        var f9 = Run.Lines(window).Count(line => line.StartsWith($"KEY {F9} ", StringComparison.Ordinal));
        if (mode == "one_shot")
        {
            Assert.Equal(2, f9);
        }
        else
        {
            Assert.Equal(clientSeesKey ? 4 : 0, f9);
        }
    }
}
