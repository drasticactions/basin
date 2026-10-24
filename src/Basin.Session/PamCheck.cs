using System.Runtime.InteropServices;
using System.Text;
using Basin;

namespace Basin.Session;

/// <summary>
/// Checks a password with a PAM helper process, without a block on the compositor loop. The helper reads the password on stdin and writes "1" on success.
/// </summary>
public sealed unsafe class PamCheck : IDisposable
{
    private const int CloseOnExec = 0x80000;
    private const int WaitNoHang = 1;
    private const int SignalKill = 9;

    private readonly ICompositorEventLoop _loop;
    private readonly Action<bool> _done;
    private IEventSource? _source;
    private IEventSource? _reaper;
    private int _zombie;
    private int _read = -1;
    private int _pid;
    private bool _finished;

    private PamCheck(ICompositorEventLoop loop, Action<bool> done)
    {
        _loop = loop;
        _done = done;
    }

    /// <summary>
    /// Starts the helper process and writes the password to it. The result arrives later through <paramref name="done"/> on the loop thread.
    /// </summary>
    /// <param name="loop">The loop that watches the helper output and reaps the process.</param>
    /// <param name="argv">The helper command line. The first item is the program, which the PATH search finds.</param>
    /// <param name="password">The password bytes. The helper gets the bytes up to the first zero byte.</param>
    /// <param name="done">The callback that gets <see langword="true"/> when the helper accepted the password. It is not called after <see cref="Dispose"/>.</param>
    /// <param name="error">The reason for failure when the result is <see langword="null"/>, otherwise <see langword="null"/>.</param>
    /// <returns>The running check, which the caller owns and must dispose, or <see langword="null"/> when the process did not start.</returns>
    public static PamCheck? Start(ICompositorEventLoop loop, IReadOnlyList<string> argv, byte[] password, Action<bool> done, out string? error)
    {
        var check = new PamCheck(loop, done);
        if (!check.Spawn(argv, password, out error))
        {
            check.Dispose();
            return null;
        }

        return check;
    }

    private bool Spawn(IReadOnlyList<string> argv, byte[] password, out string? error)
    {
        var input = stackalloc int[2];
        var output = stackalloc int[2];
        if (PamNative.Pipe2(input, CloseOnExec) != 0)
        {
            error = $"pipe: {Marshal.GetLastPInvokeError()}";
            return false;
        }

        if (PamNative.Pipe2(output, CloseOnExec) != 0)
        {
            error = $"pipe: {Marshal.GetLastPInvokeError()}";
            _ = PamNative.Close(input[0]);
            _ = PamNative.Close(input[1]);
            return false;
        }

        var actions = Marshal.AllocHGlobal(256);
        var blocks = new List<nint>();
        var strings = new List<nint>();
        try
        {
            _ = PamNative.FileActionsInit(actions);
            _ = PamNative.FileActionsDup2(actions, input[0], 0);
            _ = PamNative.FileActionsDup2(actions, output[1], 1);
            var argvBlock = (byte**)Marshal.AllocHGlobal(sizeof(nint) * (argv.Count + 1));
            blocks.Add((nint)argvBlock);
            for (var i = 0; i < argv.Count; i++)
            {
                var text = Marshal.StringToCoTaskMemUTF8(argv[i]);
                strings.Add(text);
                argvBlock[i] = (byte*)text;
            }

            argvBlock[argv.Count] = null;
            var environment = Environment.GetEnvironmentVariables();
            var envBlock = (byte**)Marshal.AllocHGlobal(sizeof(nint) * (environment.Count + 1));
            blocks.Add((nint)envBlock);
            var e = 0;
            foreach (System.Collections.DictionaryEntry entry in environment)
            {
                var text = Marshal.StringToCoTaskMemUTF8($"{entry.Key}={entry.Value}");
                strings.Add(text);
                envBlock[e++] = (byte*)text;
            }

            envBlock[e] = null;
            var result = PamNative.SpawnP(out _pid, argvBlock[0], actions, 0, argvBlock, envBlock);
            _ = PamNative.Close(input[0]);
            _ = PamNative.Close(output[1]);
            if (result != 0)
            {
                _ = PamNative.Close(input[1]);
                _ = PamNative.Close(output[0]);
                error = $"{argv[0]}: {Marshal.GetPInvokeErrorMessage(result)}";
                return false;
            }

            fixed (byte* secret = password)
            {
                var length = Array.IndexOf(password, (byte)0);
                length = length < 0 ? password.Length : length;
                _ = PamNative.Write(input[1], secret, (nuint)length);
                var newline = (byte)'\n';
                _ = PamNative.Write(input[1], &newline, 1);
            }

            _ = PamNative.Close(input[1]);
            _read = output[0];
            _source = _loop.AddFd(_read, FdReadiness.Readable, (_, _) => OnReadable());
            error = null;
            return true;
        }
        finally
        {
            _ = PamNative.FileActionsDestroy(actions);
            Marshal.FreeHGlobal(actions);
            foreach (var block in blocks)
            {
                Marshal.FreeHGlobal(block);
            }

            foreach (var text in strings)
            {
                Marshal.FreeCoTaskMem(text);
            }
        }
    }

    private void OnReadable()
    {
        var buffer = stackalloc byte[8];
        var count = PamNative.Read(_read, buffer, 8);
        if (count < 0)
        {
            return;
        }

        Finish(count > 0 && buffer[0] == (byte)'1');
    }

    private void Finish(bool success)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Release();
        _done(success);
    }

    private void Release()
    {
        _source?.Remove();
        _source = null;
        if (_read >= 0)
        {
            _ = PamNative.Close(_read);
            _read = -1;
        }

        if (_pid > 0 && !Reap(_pid))
        {
            _zombie = _pid;
            var attempts = 0;
            _reaper = _loop.AddTimer(() =>
            {
                if (Reap(_zombie) || ++attempts > 100)
                {
                    _zombie = 0;
                    _reaper!.Remove();
                    _reaper = null;
                    return;
                }

                _reaper!.UpdateTimer(50);
            });
            _reaper.UpdateTimer(50);
        }

        _pid = 0;
    }

    private static bool Reap(int pid)
    {
        var status = 0;
        return PamNative.WaitPid(pid, &status, WaitNoHang) != 0;
    }

    /// <summary>
    /// Cancels the check, closes the pipe and reaps the helper. A helper that is still running is killed.
    /// </summary>
    public void Dispose()
    {
        _finished = true;
        Release();
        _reaper?.Remove();
        _reaper = null;
        if (_zombie > 0)
        {
            _ = PamNative.Kill(_zombie, SignalKill);
            var status = 0;
            _ = PamNative.WaitPid(_zombie, &status, 0);
            _zombie = 0;
        }
    }
}
