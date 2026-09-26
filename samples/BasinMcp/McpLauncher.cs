using System.Diagnostics;
using System.Runtime.InteropServices;
using Basin.Ipc;
using static BasinMcp.McpLog;

namespace BasinMcp;

internal sealed class McpLauncher : IAsyncDisposable
{
    private const int SigTerm = 15;

    private readonly Process _process;
    private readonly Task _output;
    private readonly Task _errors;
    private bool _disposed;

    private McpLauncher(string directory, string socketPath, Process process)
    {
        Directory = directory;
        SocketPath = socketPath;
        _process = process;
        var sink = Console.OpenStandardError();
        _output = process.StandardOutput.BaseStream.CopyToAsync(sink);
        _errors = process.StandardError.BaseStream.CopyToAsync(sink);
        Exited = process.WaitForExitAsync();
    }

    public static TimeSpan StopGrace { get; } = TimeSpan.FromSeconds(5);

    public string Directory { get; }

    public string SocketPath { get; }

    public int Pid => _process.Id;

    public Task Exited { get; }

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.HasExited ? _process.ExitCode : 0;

    public static McpLauncher? Start(IReadOnlyList<string> command, out string? error)
    {
        if (command.Count == 0 || command[0].Length == 0)
        {
            error = "--launch needs a command after --";
            return null;
        }

        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime) || !Path.IsPathRooted(runtime))
        {
            error = "--launch needs XDG_RUNTIME_DIR for its private socket directory";
            return null;
        }

        var directory = Path.Combine(runtime, $"basin-mcp-{Environment.ProcessId}");
        try
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }

            _ = System.IO.Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"cannot make '{directory}': {exception.Message}";
            return null;
        }

        var socketPath = Path.Combine(directory, "basin.sock");
        var start = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        for (var i = 1; i < command.Count; i++)
        {
            start.ArgumentList.Add(command[i]);
        }

        start.Environment[IpcProtocol.PathVariable] = socketPath;
        _ = start.Environment.Remove(IpcProtocol.SocketVariable);
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("the process did not start");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            TryRemove(directory);
            error = $"{command[0]}: {exception.Message}";
            return null;
        }

        process.StandardInput.Close();
        Log.Info($"launched {command[0]} (pid {process.Id}) with its control socket at {socketPath}");
        error = null;
        return new McpLauncher(directory, socketPath, process);
    }

    public async Task<bool> WaitForSocketAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(SocketPath))
            {
                return true;
            }

            if (_process.HasExited)
            {
                return false;
            }

            await Task.WhenAny(Exited, Task.Delay(20, cancellationToken)).ConfigureAwait(false);
        }

        return File.Exists(SocketPath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_process.HasExited)
        {
            Log.Info($"stopping pid {_process.Id}");
            _ = Kill(_process.Id, SigTerm);
            TryRemove(Directory);
            if (await Task.WhenAny(Exited, Task.Delay(StopGrace)).ConfigureAwait(false) != Exited)
            {
                Log.Warn($"pid {_process.Id} ignored SIGTERM for {StopGrace.TotalSeconds:0} s; killing it");
                _process.Kill();
                await Exited.ConfigureAwait(false);
            }
        }

        try
        {
            await Task.WhenAll(_output, _errors).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }

        _process.Dispose();
        TryRemove(Directory);
    }

    private static void TryRemove(string directory)
    {
        try
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"could not remove '{directory}': {exception.Message}");
        }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
