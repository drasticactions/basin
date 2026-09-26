using System.Diagnostics;
using System.Text.Json;
using Basin.Tests;
using Xunit;

namespace BasinMcp.Tests;

public sealed class McpLaunchTests : IDisposable
{
    private readonly DirectoryInfo _runtime = Directory.CreateTempSubdirectory("basin-mcp-launch-");

    public void Dispose() => _runtime.Delete(recursive: true);

    private Process Start(params string[] command)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "basin-mcp"))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["XDG_RUNTIME_DIR"] = _runtime.FullName;
        start.ArgumentList.Add("--log-level");
        start.ArgumentList.Add("debug");
        start.ArgumentList.Add("--launch");
        start.ArgumentList.Add("--");
        foreach (var word in command)
        {
            start.ArgumentList.Add(word);
        }

        return Process.Start(start)!;
    }

    private static T Wait<T>(Task<T> task, Action? pump = null, int milliseconds = 15_000)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (pump is null)
            {
                Thread.Sleep(5);
            }
            else
            {
                pump();
            }
        }

        Assert.True(task.IsCompleted, "the wait timed out");
        return task.GetAwaiter().GetResult();
    }

    [Fact]
    public void Launch_serves_the_childs_socket_and_removes_everything_when_stdin_closes()
    {
        var reported = Path.Combine(_runtime.FullName, "reported");
        using var process = Start("sh", "-c", $"printf %s \"$BASIN_IPC_PATH\" > {reported}; echo noise-on-stdout; echo noise-on-stderr >&2; exec sleep 60");
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var deadline = Environment.TickCount64 + 15_000;
        while ((!File.Exists(reported) || new FileInfo(reported).Length == 0) && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(10);
        }

        var socket = File.ReadAllText(reported);
        var directory = Path.GetDirectoryName(socket)!;
        Assert.Equal(Path.Combine(_runtime.FullName, $"basin-mcp-{process.Id}", "basin.sock"), socket);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));

        using var rig = IpcFullRig.Create(socket, listen: true);
        rig.Server.Start();
        void Pump() => rig.Host.Loop.Dispatch(5);

        string[] requests =
        [
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"session_describe","arguments":{}}}""",
        ];
        var lines = new List<string>();
        foreach (var request in requests)
        {
            process.StandardInput.WriteLine(request);
            process.StandardInput.Flush();
            if (request.Contains("\"id\"", StringComparison.Ordinal))
            {
                lines.Add(Wait(process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask(), Pump)!);
            }
        }

        Assert.Contains("\"compositor\":\"full\"", lines[1].Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        process.StandardInput.Close();
        var tail = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        _ = Wait(process.WaitForExitAsync(TestContext.Current.CancellationToken).ContinueWith(_ => 0, TaskScheduler.Default), Pump);
        Assert.Equal(0, process.ExitCode);
        lines.AddRange(Wait(tail).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        foreach (var line in lines)
        {
            using var frame = JsonDocument.Parse(line);
            Assert.Equal("2.0", frame.RootElement.GetProperty("jsonrpc").GetString());
        }

        var errors = Wait(stderr);
        Assert.Contains("noise-on-stdout", errors, StringComparison.Ordinal);
        Assert.Contains("noise-on-stderr", errors, StringComparison.Ordinal);
        Assert.Contains("stopping pid", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory));
        var launched = System.Text.RegularExpressions.Regex.Match(errors, @"launched sh \(pid (\d+)\)");
        Assert.True(launched.Success, errors);
        Assert.False(Directory.Exists($"/proc/{launched.Groups[1].Value}"));
    }

    [Fact]
    public void An_interrupt_then_a_terminate_stops_the_child_and_removes_the_directory()
    {
        var reported = Path.Combine(_runtime.FullName, "reported");
        using var process = Start("sh", "-c", $"printf %s \"$BASIN_IPC_PATH\" > {reported}; trap 'kill $!; sleep 1; exit 0' TERM; sleep 60 & wait");
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var deadline = Environment.TickCount64 + 15_000;
        while ((!File.Exists(reported) || new FileInfo(reported).Length == 0) && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(10);
        }

        var socket = File.ReadAllText(reported);
        using var rig = IpcFullRig.Create(socket, listen: true);
        rig.Server.Start();
        void Pump() => rig.Host.Loop.Dispatch(5);
        Thread.Sleep(200);
        Assert.Equal(0, Signal(process.Id, 2));
        Thread.Sleep(100);
        _ = Signal(process.Id, 15);
        Thread.Sleep(400);
        _ = Signal(process.Id, 9);
        _ = Wait(process.WaitForExitAsync(TestContext.Current.CancellationToken).ContinueWith(_ => 0, TaskScheduler.Default), Pump);
        var errors = Wait(stderr);
        Assert.Contains("stopping pid", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(socket)));
        var launched = System.Text.RegularExpressions.Regex.Match(errors, @"launched sh \(pid (\d+)\)");
        Assert.True(launched.Success, errors);
        var child = $"/proc/{launched.Groups[1].Value}";
        var deadline2 = Environment.TickCount64 + 5_000;
        while (IsLive(child) && Environment.TickCount64 < deadline2)
        {
            Pump();
        }

        Assert.False(IsLive(child));
    }

    private static bool IsLive(string proc) =>
        File.Exists(Path.Combine(proc, "stat")) && !File.ReadAllText(Path.Combine(proc, "stat")).Split(' ')[2].Equals("Z", StringComparison.Ordinal);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill")]
    private static extern int Signal(int pid, int signal);

    [Fact]
    public void Launch_exits_with_the_status_of_a_child_that_ends_first()
    {
        using var process = Start("sh", "-c", "exit 7");
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        _ = Wait(process.WaitForExitAsync(TestContext.Current.CancellationToken).ContinueWith(_ => 0, TaskScheduler.Default));
        Assert.Equal(7, process.ExitCode);
        Assert.Contains("exited with status 7", Wait(stderr), StringComparison.Ordinal);
        Assert.Empty(_runtime.GetDirectories());
    }

    [Fact]
    public void Launch_without_a_command_is_a_usage_error()
    {
        using var process = Start();
        _ = Wait(process.WaitForExitAsync(TestContext.Current.CancellationToken).ContinueWith(_ => 0, TaskScheduler.Default));
        Assert.Equal(1, process.ExitCode);
    }
}
