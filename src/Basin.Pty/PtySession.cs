using System.Buffers;
using System.Collections;
using Basin.Diagnostics;
using static Basin.Pty.PtyLog;

namespace Basin.Pty;

/// <summary>
/// A child process on a pseudo-terminal, driven from the compositor event loop with no threads. All members and events run on the compositor thread.
/// </summary>
public sealed class PtySession : IDisposable
{
    /// <summary>
    /// The largest number of bytes that one readiness callback reads from the terminal before it returns to the loop.
    /// </summary>
    public const int ReadBudget = 64 * 1024;

    private const int ExitDrainMs = 100;
    private const int ReapPollMs = 20;

    private readonly ICompositorEventLoop _loop;
    private readonly byte[] _buffer;
    private int _master;
    private int _pidfd;
    private IEventSource? _masterWatch;
    private IEventSource? _pidWatch;
    private IEventSource? _timer;
    private byte[] _pending = [];
    private int _pendingStart;
    private int _pendingLength;
    private bool _masterEof;
    private PtyExit? _exit;
    private bool _finished;
    private bool _disposed;

    private PtySession(ICompositorEventLoop loop, int master, int pid)
    {
        _loop = loop;
        _master = master;
        _pidfd = -1;
        ProcessId = pid;
        _buffer = ArrayPool<byte>.Shared.Rent(ReadBudget);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the process ID of the child. The child also leads its own session and process group with this ID.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    /// Gets a value indicating whether the session is alive. It is <see langword="false"/> after <see cref="Exited"/> fires or after <see cref="Dispose"/>.
    /// </summary>
    public bool Running => !_finished && !_disposed;

    /// <summary>
    /// Gets the way the child ended. It is <see langword="null"/> until <see cref="Exited"/> fires.
    /// </summary>
    public PtyExit? ExitState => _finished ? _exit : null;

    /// <summary>
    /// Gets the number of bytes that <see cref="Write"/> accepted but the terminal has not taken yet.
    /// </summary>
    public int QueuedBytes => _pendingLength;

    /// <summary>
    /// Gets the number of children of disposed sessions that the library has not reaped yet.
    /// </summary>
    public static int Orphans => PtyReaper.Pending;

    /// <summary>
    /// Occurs when the child writes to the terminal. The span is valid only during the call, and each call carries at most <see cref="ReadBudget"/> bytes.
    /// </summary>
    public event Action<ReadOnlySpan<byte>>? Output;

    /// <summary>
    /// Occurs once, after the child is reaped and after the last <see cref="Output"/>. It does not occur after <see cref="Dispose"/>.
    /// </summary>
    public event Action<PtyExit>? Exited;

    /// <summary>
    /// Opens a pseudo-terminal, sets its size, and spawns the program as the leader of a new session on it.
    /// </summary>
    /// <param name="loop">The loop that watches the terminal and the child.</param>
    /// <param name="start">The program, its environment and the initial terminal size.</param>
    /// <param name="failure">The reason when the start fails, otherwise <see langword="null"/>.</param>
    /// <returns>The running session that the caller must dispose, or <see langword="null"/> when the terminal or the spawn fails.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> or <paramref name="start"/> is <see langword="null"/>.</exception>
    public static PtySession? TryStart(ICompositorEventLoop loop, PtyStartInfo start, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(start);
        if (start.Argv.Count == 0 || string.IsNullOrEmpty(start.Argv[0]))
        {
            failure = "no program to run";
            return null;
        }

        var master = PtyNative.OpenMaster(out var slave, out failure);
        if (master < 0)
        {
            return null;
        }

        PtyNative.SetUtf8(slave!);
        _ = PtyNative.SetWindow(master, start.Columns, start.Rows, start.PixelWidth, start.PixelHeight);
        var pid = PtyNative.Spawn(start.Argv, EnvironmentOf(start.Environment), start.WorkingDirectory, slave!, out failure);
        if (pid < 0)
        {
            _ = PtyNative.Close(master);
            return null;
        }

        var session = new PtySession(loop, master, pid);
        session.Watch();
        return session;
    }

    /// <summary>
    /// Blocks until the children of disposed sessions are reaped, and kills the ones still alive after the grace period. Call it at exit, when the loop does not dispatch again.
    /// </summary>
    /// <param name="grace">The time to wait before the remaining children get <c>SIGKILL</c>.</param>
    public static void CollectOrphans(TimeSpan grace) => PtyReaper.Collect(grace);

    /// <summary>
    /// Sends bytes to the child as terminal input. Bytes that the terminal cannot take now are queued and sent when it becomes writable, so the call never blocks.
    /// </summary>
    /// <param name="data">The bytes to send. The call copies what it queues. After exit or dispose, the call ignores the data.</param>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed || _finished || data.IsEmpty)
        {
            return;
        }

        if (_pendingLength == 0)
        {
            var written = PtyNative.Write(_master, data, out var errno);
            if (written < 0)
            {
                if (errno != PtyNative.EAgain)
                {
                    Log.Debug($"pid {ProcessId}: terminal write failed, errno {errno}");
                    return;
                }

                written = 0;
            }

            data = data[written..];
            if (data.IsEmpty)
            {
                return;
            }
        }

        Queue(data);
        _masterWatch?.UpdateFd(FdReadiness.Readable | FdReadiness.Writable);
    }

    /// <summary>
    /// Sets the terminal size, which sends <c>SIGWINCH</c> to the foreground process group. It does nothing after <see cref="Dispose"/>.
    /// </summary>
    /// <param name="columns">The width in character cells.</param>
    /// <param name="rows">The height in character cells.</param>
    /// <param name="pixelWidth">The width in pixels, or 0 when unknown.</param>
    /// <param name="pixelHeight">The height in pixels, or 0 when unknown.</param>
    public void Resize(int columns, int rows, int pixelWidth, int pixelHeight)
    {
        if (_disposed || _master < 0)
        {
            return;
        }

        _ = PtyNative.SetWindow(_master, columns, rows, pixelWidth, pixelHeight);
    }

    /// <summary>
    /// Sends a signal to the child's process group. It does nothing after the child is reaped or after <see cref="Dispose"/>.
    /// </summary>
    /// <param name="signal">The POSIX signal number.</param>
    public void Signal(int signal)
    {
        if (_disposed || _exit is not null)
        {
            return;
        }

        _ = PtyNative.Kill(-ProcessId, signal);
    }

    /// <summary>
    /// Sends <c>SIGHUP</c> to the child's process group and closes the terminal. A child that is still alive is reaped later on the loop, and gets <c>SIGKILL</c> after 3 seconds.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _masterWatch?.Remove();
        _masterWatch = null;
        _pidWatch?.Remove();
        _pidWatch = null;
        _timer?.Remove();
        _timer = null;
        if (_exit is null)
        {
            _ = PtyNative.Kill(-ProcessId, PtyNative.SignalHangup);
            _ = PtyNative.Kill(ProcessId, PtyNative.SignalHangup);
        }

        _ = PtyNative.Close(_master);
        _master = -1;
        if (_exit is null && !PtyNative.TryReap(ProcessId, out _))
        {
            PtyReaper.Adopt(_loop, ProcessId, _pidfd);
        }
        else if (_pidfd >= 0)
        {
            _ = PtyNative.Close(_pidfd);
        }

        _pidfd = -1;
        ArrayPool<byte>.Shared.Return(_buffer);
        BasinCounters.Untrack();
    }

    private static List<string> EnvironmentOf(IReadOnlyDictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                values[key] = value;
            }
        }

        foreach (var (key, value) in overrides)
        {
            if (value is null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = value;
            }
        }

        var entries = new List<string>(values.Count);
        foreach (var (key, value) in values)
        {
            entries.Add($"{key}={value}");
        }

        return entries;
    }

    private void Watch()
    {
        _masterWatch = _loop.AddFd(_master, FdReadiness.Readable, OnMaster);
        _pidfd = PtyNative.PidfdOpenOf(ProcessId);
        if (_pidfd < 0)
        {
            Log.Debug($"pid {ProcessId}: pidfd_open failed, the child is reaped after its terminal closes");
            return;
        }

        _pidWatch = _loop.AddFd(_pidfd, FdReadiness.Readable, OnPidfd);
    }

    private void OnMaster(int fd, FdReadiness readiness)
    {
        try
        {
            if ((readiness & FdReadiness.Writable) != 0)
            {
                Flush();
            }

            if ((readiness & (FdReadiness.Readable | FdReadiness.Hangup | FdReadiness.Error)) != 0)
            {
                Drain(ReadBudget);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"pid {ProcessId}: a terminal observer failed");
        }
    }

    private void OnPidfd(int fd, FdReadiness readiness)
    {
        try
        {
            if (!TryReap())
            {
                return;
            }

            if (_masterEof)
            {
                Finish();
            }
            else
            {
                _timer ??= _loop.AddTimer(OnTimer);
                _timer.UpdateTimer(ExitDrainMs);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"pid {ProcessId}: a terminal observer failed");
        }
    }

    private void OnTimer()
    {
        try
        {
            if (_disposed || _finished)
            {
                return;
            }

            if (_exit is not null)
            {
                Drain(ReadBudget);
                Finish();
                return;
            }

            if (TryReap())
            {
                Finish();
            }
            else
            {
                _timer!.UpdateTimer(ReapPollMs);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"pid {ProcessId}: a terminal observer failed");
        }
    }

    private bool TryReap()
    {
        if (_exit is not null)
        {
            return true;
        }

        if (!PtyNative.TryReap(ProcessId, out var status))
        {
            return false;
        }

        _exit = PtyExit.FromWaitStatus(status);
        _pidWatch?.Remove();
        _pidWatch = null;
        if (_pidfd >= 0)
        {
            _ = PtyNative.Close(_pidfd);
            _pidfd = -1;
        }

        return true;
    }

    private void Drain(int budget)
    {
        while (budget > 0 && !_masterEof && !_disposed)
        {
            var count = PtyNative.Read(_master, _buffer.AsSpan(0, Math.Min(budget, ReadBudget)), out var errno);
            if (count > 0)
            {
                budget -= count;
                Output?.Invoke(_buffer.AsSpan(0, count));
                continue;
            }

            if (count < 0 && errno == PtyNative.EAgain)
            {
                return;
            }

            if (count < 0 && errno != PtyNative.EIo)
            {
                Log.Debug($"pid {ProcessId}: terminal read failed, errno {errno}");
            }

            OnEof();
        }
    }

    private void OnEof()
    {
        _masterEof = true;
        _masterWatch?.Remove();
        _masterWatch = null;
        if (TryReap())
        {
            Finish();
            return;
        }

        if (_pidWatch is null)
        {
            _timer ??= _loop.AddTimer(OnTimer);
            _timer.UpdateTimer(ReapPollMs);
        }
    }

    private void Finish()
    {
        if (_finished || _disposed || _exit is not { } exit)
        {
            return;
        }

        _finished = true;
        _masterWatch?.Remove();
        _masterWatch = null;
        _timer?.Remove();
        _timer = null;
        _pendingLength = 0;
        Exited?.Invoke(exit);
    }

    private void Queue(ReadOnlySpan<byte> data)
    {
        if (_pendingStart + _pendingLength + data.Length > _pending.Length)
        {
            if (_pendingLength + data.Length <= _pending.Length)
            {
                _pending.AsSpan(_pendingStart, _pendingLength).CopyTo(_pending);
            }
            else
            {
                var grown = new byte[Math.Max(_pending.Length * 2, _pendingLength + data.Length)];
                _pending.AsSpan(_pendingStart, _pendingLength).CopyTo(grown);
                _pending = grown;
            }

            _pendingStart = 0;
        }

        data.CopyTo(_pending.AsSpan(_pendingStart + _pendingLength));
        _pendingLength += data.Length;
    }

    private void Flush()
    {
        while (_pendingLength > 0)
        {
            var written = PtyNative.Write(_master, _pending.AsSpan(_pendingStart, _pendingLength), out var errno);
            if (written < 0)
            {
                if (errno == PtyNative.EAgain)
                {
                    return;
                }

                Log.Debug($"pid {ProcessId}: terminal write failed, errno {errno}");
                _pendingLength = 0;
                break;
            }

            _pendingStart += written;
            _pendingLength -= written;
        }

        _pendingStart = 0;
        _masterWatch?.UpdateFd(FdReadiness.Readable);
    }
}
