using System.Diagnostics;
using Xunit;

namespace MauiComp.Tests;

internal sealed class Session : IDisposable
{
    private readonly Process _process;
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();

    public Session(string path, string renderer = "pixman", params string[] extra)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--backend");
        info.ArgumentList.Add("headless");
        info.ArgumentList.Add("--renderer");
        info.ArgumentList.Add(renderer);
        foreach (var argument in extra)
        {
            info.ArgumentList.Add(argument);
        }

        _process = Process.Start(info)!;
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (_gate)
                {
                    _lines.Add(e.Data);
                }
            }
        };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
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

    public async Task<string?> WaitForAsync(Func<string, bool> predicate, string? poke = null, bool fresh = false)
    {
        var floor = 0;
        if (fresh)
        {
            lock (_gate)
            {
                floor = _lines.Count;
            }
        }

        for (var i = 0; i < 100; i++)
        {
            if (poke is not null)
            {
                await SendAsync(poke);
            }

            lock (_gate)
            {
                for (var j = _lines.Count - 1; j >= floor; j--)
                {
                    if (predicate(_lines[j]))
                    {
                        return _lines[j];
                    }
                }
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return null;
    }

    public async Task<List<string>> StateAsync()
    {
        var mark = 0;
        lock (_gate)
        {
            mark = _lines.Count;
        }

        await SendAsync("where");
        var end = await WaitForAsync(line => line.StartsWith("HIT ", StringComparison.Ordinal), fresh: true);
        if (end is null)
        {
            return [];
        }

        lock (_gate)
        {
            return _lines.Skip(mark).ToList();
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
