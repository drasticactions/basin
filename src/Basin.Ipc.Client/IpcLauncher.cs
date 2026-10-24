using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Basin.Ipc;

/// <summary>
/// Starts a compositor whose control socket is in a private directory under <c>XDG_RUNTIME_DIR</c>, and stops it with <c>SIGTERM</c> and then <c>SIGKILL</c>.
/// </summary>
public sealed class IpcLauncher : IAsyncDisposable
{
    private const int SigTerm = 15;
    private const int PumpChunk = 16 * 1024;
    private const string SocketName = "basin.sock";

    private readonly Process _process;
    private readonly Stream _sink;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _drain = new();
    private readonly Task _output;
    private readonly Task _errors;
    private int? _exitCode;
    private bool _disposed;

    private IpcLauncher(string directory, string socketPath, Process process, Stream output)
    {
        Directory = directory;
        SocketPath = socketPath;
        _process = process;
        _sink = output;
        Pid = process.Id;
        _output = PumpAsync(process.StandardOutput.BaseStream);
        _errors = PumpAsync(process.StandardError.BaseStream);
        Exited = RecordExitAsync();
    }

    /// <summary>
    /// Gets the time that <see cref="StopAsync"/> waits after <c>SIGTERM</c> before it kills the process.
    /// </summary>
    public static TimeSpan StopGrace { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the time that <see cref="StopAsync"/> waits for the child's output after the process exits.
    /// A client that the compositor started can keep the pipes open, so the wait has a limit.
    /// </summary>
    public static TimeSpan DrainGrace { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the private directory that holds the socket. <see cref="StopAsync"/> deletes it.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Gets the socket path that the compositor gets through <c>BASIN_IPC_PATH</c>.
    /// </summary>
    public string SocketPath { get; }

    /// <summary>
    /// Gets the process id of the compositor. It stays valid after <see cref="StopAsync"/>.
    /// </summary>
    public int Pid { get; }

    /// <summary>
    /// Gets a task that completes when the process exits.
    /// </summary>
    public Task Exited { get; }

    /// <summary>
    /// Gets a value indicating whether the process exited. It stays valid after <see cref="StopAsync"/>.
    /// </summary>
    public bool HasExited => Exited.IsCompleted;

    /// <summary>
    /// Gets the exit code of the process, or <see langword="null"/> while the process runs. It stays valid after <see cref="StopAsync"/>.
    /// </summary>
    public int? ExitCode => Exited.IsCompleted ? _exitCode : null;

    /// <summary>
    /// Starts a compositor with no shell. The child's stdin is closed. A stale directory with the same name that holds only the socket is removed first.
    /// Any other file at that path refuses the start.
    /// </summary>
    /// <param name="command">The program and its arguments.</param>
    /// <param name="directoryName">The name of the private directory under <c>XDG_RUNTIME_DIR</c>. It must be one path segment, not <c>.</c> or <c>..</c>.</param>
    /// <param name="output">The stream that takes the child's stdout and stderr. The launcher writes to it from two tasks, one write at a time.
    /// Lines of stdout and stderr stay whole, except a line longer than 16 KiB.</param>
    /// <param name="error">The reason for a failure, or <see langword="null"/> on success.</param>
    /// <returns>The launcher, or <see langword="null"/> when the name is not valid, or the directory or the process cannot be made.</returns>
    public static IpcLauncher? Start(IReadOnlyList<string> command, string directoryName, Stream output, out string? error)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrEmpty(directoryName);
        ArgumentNullException.ThrowIfNull(output);
        if (command.Count == 0 || command[0].Length == 0)
        {
            error = "the launch needs a command";
            return null;
        }

        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime) || !Path.IsPathRooted(runtime))
        {
            error = "the launch needs XDG_RUNTIME_DIR for its private socket directory";
            return null;
        }

        if (directoryName is "." or ".." || directoryName.Contains('/', StringComparison.Ordinal) || directoryName.Contains('\0', StringComparison.Ordinal))
        {
            error = $"'{directoryName}' is not one path segment";
            return null;
        }

        var directory = Path.Combine(runtime, directoryName);
        try
        {
            if (!TryRemoveStale(directory, out error))
            {
                return null;
            }

            _ = System.IO.Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"cannot make '{directory}': {exception.Message}";
            return null;
        }

        var socketPath = Path.Combine(directory, SocketName);
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
        error = null;
        return new IpcLauncher(directory, socketPath, process, output);
    }

    /// <summary>
    /// Waits until the socket file exists.
    /// </summary>
    /// <param name="timeout">The longest wait.</param>
    /// <param name="cancellationToken">A token that ends the wait early.</param>
    /// <returns><see langword="false"/> when the process exits, the time runs out or the token is canceled first.</returns>
    public async Task<bool> WaitForSocketAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(SocketPath))
            {
                return true;
            }

            if (HasExited)
            {
                return false;
            }

            await Task.WhenAny(Exited, Task.Delay(20, cancellationToken)).ConfigureAwait(false);
        }

        return File.Exists(SocketPath);
    }

    /// <summary>
    /// Sends <c>SIGTERM</c>, kills the process after <see cref="StopGrace"/>, waits up to <see cref="DrainGrace"/> for its output to drain and deletes the directory.
    /// A second call does nothing.
    /// </summary>
    /// <returns><see langword="true"/> when the process had to be killed.</returns>
    public async Task<bool> StopAsync()
    {
        if (_disposed)
        {
            return false;
        }

        _disposed = true;
        var killed = false;
        if (!_process.HasExited)
        {
            _ = Kill(Pid, SigTerm);
            TryRemove(Directory);
            if (await Task.WhenAny(Exited, Task.Delay(StopGrace)).ConfigureAwait(false) != Exited)
            {
                killed = true;
                _process.Kill();
                await Exited.ConfigureAwait(false);
            }
        }

        await Exited.ConfigureAwait(false);
        var drained = Task.WhenAll(_output, _errors);
        if (await Task.WhenAny(drained, Task.Delay(DrainGrace)).ConfigureAwait(false) != drained)
        {
            await _drain.CancelAsync().ConfigureAwait(false);
            _process.StandardOutput.Dispose();
            _process.StandardError.Dispose();
        }

        try
        {
            await drained.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }

        _process.Dispose();
        _drain.Dispose();
        _gate.Dispose();
        TryRemove(Directory);
        return killed;
    }

    /// <summary>
    /// Stops the compositor, as <see cref="StopAsync"/> does.
    /// </summary>
    /// <returns>A task that completes when the process is stopped.</returns>
    public async ValueTask DisposeAsync() => _ = await StopAsync().ConfigureAwait(false);

    private static bool TryRemoveStale(string directory, out string? error)
    {
        error = null;
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(directory);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
        {
            error = $"'{directory}' exists and is not a directory";
            return false;
        }

        if (UnixSocket.OwnerUid(directory) != UnixSocket.EffectiveUid())
        {
            error = $"'{directory}' exists and another user owns it";
            return false;
        }

        var socket = Path.Combine(directory, SocketName);
        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(directory))
        {
            if (entry != socket || (File.GetAttributes(entry) & FileAttributes.Directory) != 0)
            {
                error = $"'{directory}' exists and holds more than a stale {SocketName}";
                return false;
            }
        }

        File.Delete(socket);
        System.IO.Directory.Delete(directory);
        return true;
    }

    private async Task RecordExitAsync()
    {
        await _process.WaitForExitAsync().ConfigureAwait(false);
        _exitCode = _process.ExitCode;
    }

    private async Task PumpAsync(Stream source)
    {
        var buffer = new byte[PumpChunk];
        var held = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(held), _drain.Token).ConfigureAwait(false);
            if (read == 0)
            {
                await WriteAsync(buffer.AsMemory(0, held)).ConfigureAwait(false);
                return;
            }

            held += read;
            var end = buffer.AsSpan(0, held).LastIndexOf((byte)'\n') + 1;
            if (end == 0 && held < buffer.Length)
            {
                continue;
            }

            var whole = end == 0 ? held : end;
            await WriteAsync(buffer.AsMemory(0, whole)).ConfigureAwait(false);
            buffer.AsSpan(whole, held - whole).CopyTo(buffer);
            held -= whole;
        }
    }

    private async Task WriteAsync(ReadOnlyMemory<byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            return;
        }

        await _gate.WaitAsync(_drain.Token).ConfigureAwait(false);
        try
        {
            await _sink.WriteAsync(chunk).ConfigureAwait(false);
            await _sink.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private static void TryRemove(string directory)
    {
        try
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
