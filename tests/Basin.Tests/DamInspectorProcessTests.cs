using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class DamInspectorProcessTests
{
    private static readonly string Dam = Path.Combine(AppContext.BaseDirectory, "dam");

    public static TheoryData<string> Rows => new() { "gl", "vulkan" };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Virtual_pointer_scroll_reaches_a_window_except_under_a_panel(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var dam = DamProcess.StartWith(row, ["sleep", "120"], "--inspector");
        Assert.NotNull(await dam.WaitFor(line => line.StartsWith("INSPECTOR open=yes", StringComparison.Ordinal)));
        dam.Send("inspector close");
        Assert.NotNull(await dam.WaitFor(line => line.StartsWith("INSPECTOR open=no", StringComparison.Ordinal)));
        var display = dam.Lines.First(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)).Split(' ')[1];

        var (before, overPanel, overlay, after, where) = await InspectorScroll.Run(
            display,
            async (command, match) =>
            {
                dam.Send(command);
                return await dam.WaitFor(match);
            },
            command =>
            {
                dam.Send(command);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);
        Assert.True(before == 3 && overPanel == 0 && overlay == 3 && after == 3, $"{where}: before={before} over-panel={overPanel} overlay={overlay} after={after}");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_inspector_flag_opens_the_overlay_and_a_click_closes_a_panel(string row)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var dam = DamProcess.Start(row, "--inspector");
        Assert.NotNull(await dam.WaitFor(line => line == $"INSPECTOR open=yes output=HEADLESS-1 host={row} panels=Outputs"));

        dam.Send("inspector-panel windows on");
        Assert.NotNull(await dam.WaitFor(line => line.EndsWith("panels=Outputs,Windows", StringComparison.Ordinal)));
        await Task.Delay(300, TestContext.Current.CancellationToken);

        dam.Send("move 531 50");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        dam.Send("button 272 true");
        dam.Send("button 272 false");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        dam.Send("inspector-state");
        Assert.NotNull(await dam.WaitFor(line => line == $"INSPECTOR open=yes output=HEADLESS-1 host={row} panels=Windows"));

        dam.Send("inspector-panel outputs on");
        await Task.Delay(600, TestContext.Current.CancellationToken);
        dam.Send("ipc input/touch {\"kind\":\"down\",\"id\":1,\"x\":531,\"y\":50}");
        dam.Send("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        dam.Send("ipc input/touch {\"kind\":\"up\",\"id\":1}");
        dam.Send("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        dam.Send("inspector-state");
        Assert.True(await dam.WaitFor(line => line == $"INSPECTOR open=yes output=HEADLESS-1 host={row} panels=Windows") is not null, "a touch on the close box did not close Outputs");

        await dam.Quit();
    }

    [Fact]
    public async Task Without_the_flag_the_inspector_methods_are_not_registered()
    {
        using var dam = DamProcess.Start("pixman");
        Assert.NotNull(await dam.WaitFor(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        dam.Send("inspector open");
        var reply = await dam.WaitFor(line => line.StartsWith("ERR ", StringComparison.Ordinal) || line.StartsWith("INSPECTOR", StringComparison.Ordinal));
        Assert.NotNull(reply);
        Assert.StartsWith("ERR ", reply!, StringComparison.Ordinal);
        Assert.DoesNotContain(dam.Lines, line => line.StartsWith("INSPECTOR", StringComparison.Ordinal));
        await dam.Quit();
    }

    internal sealed class DamProcess : IDisposable
    {
        private readonly Process _process;
        private readonly BlockingCollection<string> _queue = [];

        private DamProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    Lines.Enqueue(line);
                    _queue.Add(line);
                }
            };
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public ConcurrentQueue<string> Lines { get; } = new();

        public static DamProcess Start(string renderer, params string[] args) => StartWith(renderer, ["weston-simple-shm"], args);

        public static DamProcess StartWith(string renderer, string[] application, params string[] args)
        {
            Assert.SkipWhen(!File.Exists(Dam), "dam was not copied beside the tests");
            Assert.SkipWhen(FindOnPath("weston-simple-shm") is null, "weston-simple-shm is not installed");
            var start = new ProcessStartInfo(Dam)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            foreach (var arg in (string[])["--backend", "headless", "--renderer", renderer, "--ipc", "false", .. args, "--", .. application])
            {
                start.ArgumentList.Add(arg);
            }

            start.Environment["BASIN_MUTE"] = "1";
            start.Environment["BASIN_RENDER_NODE"] = CompositorTestHost.RenderNodePath;
            start.Environment.Remove("WAYLAND_DISPLAY");
            start.Environment.Remove("BASIN_RENDERER");
            return new DamProcess(Process.Start(start)!);
        }

        public void Send(string line)
        {
            _process.StandardInput.WriteLine(line);
            _process.StandardInput.Flush();
        }

        public async Task<string?> WaitFor(Func<string, bool> match, int timeoutMs = 15_000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (_queue.TryTake(out var line, 100))
                {
                    if (match(line))
                    {
                        return line;
                    }
                }
                else
                {
                    await Task.Yield();
                }
            }

            return null;
        }

        public async Task Quit()
        {
            Send("quit");
            await _process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Contains(Lines, line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }

            _process.Dispose();
            _queue.Dispose();
        }

        private static string? FindOnPath(string name)
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':'))
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
