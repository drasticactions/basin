using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace Basin.Tests.ImGui;

public sealed class InletInspectorTests
{
    public static TheoryData<string> Rows => new() { "gl", "vulkan" };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_inspector_opens_by_word_and_a_click_closes_a_panel(string row)
    {
        using var inlet = InletProcess.Start(row);
        await inlet.SendAsync("inspector open");
        var opened = await inlet.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal));
        Assert.NotNull(opened);
        Assert.StartsWith("INSPECTOR open=yes", opened!, StringComparison.Ordinal);
        Assert.Contains($"host={row}", opened!, StringComparison.Ordinal);

        await inlet.SendAsync("inspector-panel windows on");
        var shown = await inlet.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal));
        Assert.Contains("panels=Outputs,Windows", shown!, StringComparison.Ordinal);

        await inlet.SettleAsync();
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await inlet.SendAsync("move 531 50");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await inlet.SendAsync("button 272 true");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await inlet.SendAsync("button 272 false");
        await Task.Delay(400, TestContext.Current.CancellationToken);

        await inlet.SendAsync("inspector-state");
        var state = await inlet.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal));
        Assert.NotNull(state);
        Assert.Contains("panels=Windows", state!, StringComparison.Ordinal);
        Assert.DoesNotContain("Outputs", state!, StringComparison.Ordinal);

        await inlet.SendAsync("inspector-panel outputs on");
        await Task.Delay(600, TestContext.Current.CancellationToken);
        await inlet.SendAsync("ipc input/touch {\"kind\":\"down\",\"id\":1,\"x\":531,\"y\":50}");
        await inlet.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await inlet.SendAsync("ipc input/touch {\"kind\":\"up\",\"id\":1}");
        await inlet.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await inlet.SendAsync("inspector-state");
        var touched = await inlet.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal) && line.Contains("panels=Windows", StringComparison.Ordinal) && !line.Contains("Outputs", StringComparison.Ordinal));
        Assert.True(touched is not null, "a touch on the close box did not close Outputs");

        await inlet.SendAsync("inspector close");
        var closed = await inlet.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal));
        Assert.StartsWith("INSPECTOR open=no", closed!, StringComparison.Ordinal);
        await inlet.QuitAsync();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Virtual_pointer_scroll_reaches_a_window_except_under_a_panel(string row)
    {
        using var inlet = InletProcess.Start(row);
        var manager = InletProcess.Manager();
        Assert.SkipWhen(manager is null, "the inletwm binary is not built");
        var start = new System.Diagnostics.ProcessStartInfo(manager!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["WAYLAND_DISPLAY"] = inlet.Display;
        using var wm = System.Diagnostics.Process.Start(start)!;
        wm.OutputDataReceived += (_, _) => { };
        wm.ErrorDataReceived += (_, _) => { };
        wm.BeginOutputReadLine();
        wm.BeginErrorReadLine();
        try
        {
            var (before, overPanel, overlay, after, where) = await InspectorScroll.Run(
                inlet.Display,
                async (command, match) =>
                {
                    await inlet.SendAsync(command);
                    return await inlet.WaitForAsync(match);
                },
                inlet.SendAsync,
                TestContext.Current.CancellationToken);
            Assert.True(before == 3 && overPanel == 0 && overlay == 3 && after == 3, $"{where}: before={before} over-panel={overPanel} overlay={overlay} after={after}");
        }
        finally
        {
            wm.Kill(entireProcessTree: true);
            wm.WaitForExit();
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_inspector_flag_opens_it_at_start(string row)
    {
        using var inlet = InletProcess.Start(row, "--inspector");
        await inlet.SettleAsync();
        await inlet.SendAsync("inspector-state");
        var state = await inlet.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal));
        Assert.NotNull(state);
        Assert.StartsWith("INSPECTOR open=yes", state!, StringComparison.Ordinal);
        Assert.Contains($"host={row}", state!, StringComparison.Ordinal);
        await inlet.QuitAsync();
    }

    internal sealed class InletProcess : IDisposable
    {
        private readonly Process _process;
        private readonly BlockingCollection<string> _lines = [];

        private InletProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    _lines.Add(line);
                }
            };
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public static InletProcess Start(string row, params string[] extra)
        {
            Assert.SkipUnless(RendererRows.IsRunnable(row), $"the {row} row cannot run here");
            var binary = Binary();
            Assert.SkipWhen(binary is null, "the inlet binary is not built");
            var start = new ProcessStartInfo(binary!)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "--backend", "headless", "--renderer", row, "--ipc", "false", "-c", "true" })
            {
                start.ArgumentList.Add(arg);
            }

            foreach (var arg in extra)
            {
                start.ArgumentList.Add(arg);
            }

            start.Environment["WAYLAND_DISPLAY"] = string.Empty;
            start.Environment["BASIN_MUTE"] = "1";
            var inlet = new InletProcess(Process.Start(start)!);
            var socket = inlet.WaitFor(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), TimeSpan.FromSeconds(30));
            Assert.NotNull(socket);
            inlet.Display = socket!.Split(' ')[1];
            return inlet;
        }

        public string Display { get; private set; } = string.Empty;

        public async Task SendAsync(string line)
        {
            await _process.StandardInput.WriteLineAsync(line);
            await _process.StandardInput.FlushAsync();
        }

        public async Task SettleAsync()
        {
            await SendAsync("settle");
            Assert.NotNull(await WaitForAsync(line => line.StartsWith("OK session/settle", StringComparison.Ordinal)));
        }

        public Task<string?> WaitForAsync(Func<string, bool> match) =>
            Task.Run(() => WaitFor(match, TimeSpan.FromSeconds(15)));

        public async Task QuitAsync()
        {
            await SendAsync("quit");
            Assert.True(_process.WaitForExit(TimeSpan.FromSeconds(20)), "inlet did not exit on quit");
            Assert.Equal(0, _process.ExitCode);
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }

            _process.Dispose();
            _lines.Dispose();
        }

        private string? WaitFor(Func<string, bool> match, TimeSpan timeout)
        {
            var end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < end)
            {
                if (_lines.TryTake(out var line, TimeSpan.FromMilliseconds(100)) && match(line))
                {
                    return line;
                }
            }

            return null;
        }

        public static string? Manager() => Sample("InletWm", "inletwm");

        private static string? Binary() => Sample("Inlet", "inlet");

        private static string? Sample(string project, string name)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "basin.slnx")))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                return null;
            }

            var configuration = AppContext.BaseDirectory.Contains("/Release/", StringComparison.Ordinal) ? "Release" : "Debug";
            var path = Path.Combine(directory.FullName, "samples", project, "bin", configuration, "net10.0", name);
            return File.Exists(path) ? path : null;
        }
    }
}
