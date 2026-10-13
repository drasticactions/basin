using System.Diagnostics;
using Xunit;

namespace LunaWm.Tests;

public sealed class LunaSession : IDisposable
{
    public static string? Which(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':'))
        {
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name)))
            {
                return Path.Combine(directory, name);
            }
        }

        return null;
    }

    public static string? Locate(string name)
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

    public static string Compositor()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor is Linux only");
        var compositor = Locate("luna-wm");
        Assert.SkipWhen(compositor is null, "luna-wm has not been built beside the tests");
        return compositor!;
    }

    public static string Client(string name)
    {
        var client = Which(name);
        Assert.SkipWhen(client is null, $"{name} is not installed");
        return client!;
    }

    private readonly Process _process;
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LunaSession(string path, string config, IReadOnlyDictionary<string, string>? environment = null, params string[] extra)
        : this("pixman", path, config, environment, extra)
    {
    }

    public LunaSession(string renderer, string path, string config, IReadOnlyDictionary<string, string>? environment, params string[] extra)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--backend", "headless", "--renderer", renderer, "--xwayland", "false", "--config", config }.Concat(extra))
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[name] = value;
        }

        _process = Process.Start(info)!;
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                TaskCompletionSource arrived;
                lock (_gate)
                {
                    _lines.Add(e.Data);
                    arrived = _arrived;
                    _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                arrived.TrySetResult();
            }
        };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public Process Process => _process;

    public List<string> Lines()
    {
        lock (_gate)
        {
            return [.. _lines];
        }
    }

    public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);

    public async Task SendAsync(string command)
    {
        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    public int Mark()
    {
        lock (_gate)
        {
            return _lines.Count;
        }
    }

    public async Task SettleAsync()
    {
        var mark = Mark();
        await SendAsync("settle");
        var reply = await WaitForAsync(
            line => line.StartsWith("OK session/settle ", StringComparison.Ordinal) || line.StartsWith("ERR ", StringComparison.Ordinal),
            since: mark);
        Assert.True(reply is not null && reply.StartsWith("OK ", StringComparison.Ordinal), $"settle answered [{reply}]");
    }

    public async Task<string?> WaitForAsync(
        Func<string, bool> predicate,
        string? poke = null,
        bool fresh = false,
        int attempts = 100,
        int since = 0)
    {
        var floor = since;
        if (fresh)
        {
            lock (_gate)
            {
                floor = _lines.Count;
            }
        }

        var deadline = Environment.TickCount64 + (attempts * 100L);
        var cancel = TestContext.Current.CancellationToken;
        while (true)
        {
            if (poke is not null)
            {
                await SendAsync(poke);
            }

            var slotEnd = poke is null ? deadline : Math.Min(deadline, Environment.TickCount64 + 100);
            while (true)
            {
                Task next;
                lock (_gate)
                {
                    for (var j = _lines.Count - 1; j >= floor; j--)
                    {
                        if (predicate(_lines[j]))
                        {
                            return _lines[j];
                        }
                    }

                    next = _arrived.Task;
                }

                var remaining = slotEnd - Environment.TickCount64;
                if (remaining <= 0)
                {
                    break;
                }

                await Task.WhenAny(next, Task.Delay((int)remaining, cancel));
                cancel.ThrowIfCancellationRequested();
            }

            if (Environment.TickCount64 >= deadline)
            {
                return null;
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            _process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
    }
}
