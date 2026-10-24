using System.Diagnostics;
using Xunit;

namespace MauiComp.Tests;

internal sealed class Session : IDisposable
{
    private readonly Process _process;
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Session(string path, string renderer = "pixman", params string[] extra)
        : this(path, null, renderer, extra)
    {
    }

    public Session(string path, IReadOnlyDictionary<string, string>? environment, string renderer = "pixman", params string[] extra)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[name] = value;
        }

        info.ArgumentList.Add("--backend");
        info.ArgumentList.Add("headless");
        info.ArgumentList.Add("--renderer");
        info.ArgumentList.Add(renderer);
        if (Array.IndexOf(extra, "--config") < 0)
        {
            info.ArgumentList.Add("--config");
            info.ArgumentList.Add("false");
        }

        foreach (var argument in extra)
        {
            info.ArgumentList.Add(argument);
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

    public int Id => _process.Id;

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

    public static string? WlClient(string name)
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var clients = Path.Combine(directory, "scripts", "wlclients");
            if (File.Exists(Path.Combine(clients, "Makefile")))
            {
                var binary = Path.Combine(clients, "bin", name);
                if (!File.Exists(binary))
                {
                    var info = new ProcessStartInfo("make")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = clients,
                    };
                    info.ArgumentList.Add($"bin/{name}");
                    try
                    {
                        using var make = Process.Start(info)!;
                        make.WaitForExit(120_000);
                    }
                    catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        return null;
                    }
                }

                return File.Exists(binary) ? binary : null;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

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

    public static Process StartClient(string path, string display, params string[] arguments)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["WAYLAND_DISPLAY"] = display;
        return Process.Start(info)!;
    }

    public static Process StartListening(string path, string display, List<string> lines, params string[] arguments)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["WAYLAND_DISPLAY"] = display;
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (lines)
                {
                    lines.Add(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public async Task<string> DisplayAsync()
    {
        var socket = await WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        return socket is null ? throw new InvalidOperationException("the compositor printed no SOCKET line") : socket.Split(' ')[1];
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
        var reply = await WaitForAsync(
            line => line.StartsWith("OK session/settle ", StringComparison.Ordinal) || line.StartsWith("ERR ", StringComparison.Ordinal),
            since: mark);
        Assert.True(reply is not null && reply.StartsWith("OK ", StringComparison.Ordinal), $"settle answered [{reply}]");
    }

    public async Task<string?> WaitForAsync(Func<string, bool> predicate, string? poke = null, bool fresh = false, int since = 0)
    {
        var floor = since;
        if (fresh)
        {
            lock (_gate)
            {
                floor = _lines.Count;
            }
        }

        var deadline = Environment.TickCount64 + (100 * 100L);
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

    public async Task<List<string>> StateAsync()
    {
        var mark = Mark();
        await SendAsync("where");
        var end = await WaitForAsync(_ => WhereBlock(mark) is not null, since: mark);
        return end is null ? [] : WhereBlock(mark) ?? [];
    }

    private List<string>? WhereBlock(int mark)
    {
        lock (_gate)
        {
            var start = _lines.FindIndex(mark, line => line.StartsWith("POINTER ", StringComparison.Ordinal));
            if (start < 0)
            {
                return null;
            }

            var end = _lines.FindIndex(start, line => line.StartsWith("HIT ", StringComparison.Ordinal));
            return end < 0 ? null : _lines.GetRange(start, end - start + 1);
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
