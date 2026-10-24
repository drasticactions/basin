using System.Diagnostics;
using Xunit;

namespace TabWm.Tests;

internal sealed class Session : IDisposable
{
    private readonly Process _process;
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();
    private readonly List<Process> _clients = [];
    private readonly bool _ownsHome;
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Session(string path, string renderer = "pixman", string? home = null, IReadOnlyDictionary<string, string>? environment = null, params string[] extra)
    {
        _ownsHome = home is null;
        Home = home ?? Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["HOME"] = Home;
        info.Environment["XDG_CONFIG_HOME"] = Path.Combine(Home, ".config");
        info.Environment["XDG_STATE_HOME"] = Path.Combine(Home, ".local", "state");
        info.Environment["XDG_CACHE_HOME"] = Path.Combine(Home, ".cache");
        info.Environment["BASIN_MUTE"] = "1";
        info.Environment.Remove("WAYLAND_DISPLAY");
        info.Environment.Remove("DISPLAY");
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[name] = value;
        }

        foreach (var argument in new[] { "--backend", "headless", "--renderer", renderer })
        {
            info.ArgumentList.Add(argument);
        }

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
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public string Home { get; }

    public string? Display { get; private set; }

    public string? XDisplay { get; private set; }

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

    public int Mark()
    {
        lock (_gate)
        {
            return _lines.Count;
        }
    }

    public IReadOnlyList<string> LinesSince(int mark)
    {
        lock (_gate)
        {
            return _lines.Skip(mark).ToList();
        }
    }

    public async Task<string> DisplayAsync()
    {
        var socket = await WaitForPrefixAsync("SOCKET ") ?? throw new InvalidOperationException("the compositor printed no SOCKET line");
        Display = socket.Split(' ')[1];
        return Display;
    }

    public async Task<string> XDisplayAsync()
    {
        var x = await WaitForPrefixAsync("XWAYLAND ") ?? throw new InvalidOperationException("the compositor printed no XWAYLAND line");
        XDisplay = x.Split(' ')[1];
        return XDisplay;
    }

    public async Task SendAsync(string command)
    {
        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    public async Task<string?> WaitForAsync(Func<string, bool> predicate, int? since = null, int timeoutMs = 15_000)
    {
        var floor = since ?? 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        var cancel = TestContext.Current.CancellationToken;
        while (true)
        {
            Task next;
            lock (_gate)
            {
                for (var i = floor; i < _lines.Count; i++)
                {
                    if (predicate(_lines[i]))
                    {
                        return _lines[i];
                    }
                }

                next = _arrived.Task;
            }

            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                return null;
            }

            await Task.WhenAny(next, Task.Delay((int)remaining, cancel));
            cancel.ThrowIfCancellationRequested();
        }
    }

    public Task<string?> WaitForPrefixAsync(string prefix, int? since = null, int timeoutMs = 15_000) =>
        WaitForAsync(line => line.StartsWith(prefix, StringComparison.Ordinal), since, timeoutMs);

    public async Task<List<string>> AskAsync(string command, string prefix)
    {
        var mark = Mark();
        await SendAsync(command);
        await SendAsync("framecount");
        await WaitForPrefixAsync("FRAMECOUNT ", mark);
        return LinesSince(mark).Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    public async Task<List<string>> TabsAsync() => await AskAsync("tabs", "TAB ");

    public async Task<List<string>> WindowsAsync() => await AskAsync("windows", "WINDOW ");

    public async Task SettleAsync()
    {
        var mark = Mark();
        await SendAsync("settle");
        var reply = await WaitForAsync(line => line.StartsWith("OK session/settle", StringComparison.Ordinal) || line.StartsWith("ERR ", StringComparison.Ordinal), mark);
        Assert.True(reply is not null && reply.StartsWith("OK ", StringComparison.Ordinal), $"settle answered [{reply}]");
    }

    public async Task<string?> WaitForWindowAsync(Func<string, bool> predicate, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if ((await WindowsAsync()).FirstOrDefault(predicate) is { } match)
            {
                return match;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return null;
    }

    public async Task<int> FrameCountAsync()
    {
        var mark = Mark();
        await SendAsync("framecount");
        var line = await WaitForPrefixAsync("FRAMECOUNT ", mark);
        return int.Parse(line!.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    public Process StartClient(string path, params string[] arguments)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["WAYLAND_DISPLAY"] = Display ?? throw new InvalidOperationException("no display yet");
        info.Environment["HOME"] = Home;
        if (XDisplay is { } x)
        {
            info.Environment["DISPLAY"] = x;
        }
        else
        {
            info.Environment.Remove("DISPLAY");
        }

        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _clients.Add(process);
        return process;
    }

    public async Task QuitCleanlyAsync()
    {
        await SendAsync("quit");
        var frames = await WaitForPrefixAsync("FRAMES ");
        Assert.NotNull(frames);
        var fields = frames!.Split(' ');
        Assert.Equal("LIVE", fields[2]);
        if (fields[3] != "untracked")
        {
            Assert.Equal("0", fields[3]);
        }

        Assert.True(_process.WaitForExit(15_000), "the compositor exited after quit");
        Assert.Equal(0, _process.ExitCode);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            try
            {
                if (!client.HasExited)
                {
                    client.Kill(entireProcessTree: true);
                }

                client.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
            }

            client.Dispose();
        }

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
        if (_ownsHome)
        {
            try
            {
                Directory.Delete(Home, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
