using System.Diagnostics;
using Xunit;

namespace BasinGreeter.Tests;

internal sealed class GreeterSession : IDisposable
{
    private readonly Process _process;
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GreeterSession(IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var info = new ProcessStartInfo(Compositor())
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

        info.Environment.Remove("GREETD_SOCK");
        info.Environment["XDG_DATA_HOME"] = "/nonexistent/basin-greeter-tests";
        info.Environment["DBUS_SYSTEM_BUS_ADDRESS"] = "unix:path=/nonexistent/basin-greeter-tests/system";
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/nonexistent/basin-greeter-tests/session";
        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                info.Environment.Remove(name);
            }
            else
            {
                info.Environment[name] = value;
            }
        }

        _process = Process.Start(info)!;
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            TaskCompletionSource arrived;
            lock (_gate)
            {
                _lines.Add(e.Data);
                arrived = _arrived;
                _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            arrived.TrySetResult();
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (_gate)
                {
                    Errors.Add(e.Data);
                }
            }
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public List<string> Errors { get; } = [];

    public Process Process => _process;

    public static string Compositor()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor is Linux only");
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = System.IO.Path.Combine(directory, "basin-greeter");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = System.IO.Path.GetDirectoryName(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        }

        Assert.Skip("basin-greeter has not been built beside the tests");
        return null!;
    }

    public static string? Which(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(System.IO.Path.Combine(directory, name)))
            {
                return System.IO.Path.Combine(directory, name);
            }
        }

        return null;
    }

    public static string[] Headless(string renderer, string config, params string[] extra) =>
        ["--backend", "headless", "--renderer", renderer, "--config", config, .. extra];

    public List<string> Lines()
    {
        lock (_gate)
        {
            return [.. _lines];
        }
    }

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
        var reply = await WaitForAsync(line => line.StartsWith("OK session/settle ", StringComparison.Ordinal) || line.StartsWith("ERR ", StringComparison.Ordinal), since: mark);
        Assert.True(reply is not null && reply.StartsWith("OK ", StringComparison.Ordinal), $"settle answered [{reply}]");
    }

    public async Task<string> ExpectAsync(Func<string, bool> predicate, string what, string? poke = null, int since = 0)
    {
        var line = await WaitForAsync(predicate, poke, since: since);
        Assert.True(line is not null, $"no line for {what}; stdout:\n{string.Join('\n', Lines())}\nstderr:\n{string.Join('\n', Errors)}");
        return line!;
    }

    public Task<string> ExpectAsync(string exact, int since = 0) => ExpectAsync(line => line == exact, exact, since: since);

    public async Task<string?> WaitForAsync(Func<string, bool> predicate, string? poke = null, int attempts = 100, int since = 0)
    {
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
                    for (var j = since; j < _lines.Count; j++)
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

    public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);

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
