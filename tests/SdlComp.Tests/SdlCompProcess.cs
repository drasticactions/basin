using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace SdlComp.Tests;

internal sealed class SdlCompProcess : IDisposable
{
    private readonly BlockingCollection<string> _lines = [];
    private readonly List<Process> _clients = [];

    public SdlCompProcess(params string[] arguments)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the process rows run on Linux");
        var path = Locate("sdl-comp");
        Assert.SkipWhen(path is null, "the sdl-comp binary is not built");
        RuntimeDir = Path.Combine("/tmp", $"sdl-proc-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(RuntimeDir);
        File.SetUnixFileMode(RuntimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var info = new ProcessStartInfo(path!)
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

        info.Environment["XDG_RUNTIME_DIR"] = RuntimeDir;
        info.Environment["BASIN_MUTE"] = "1";
        info.Environment["SDL_VIDEO_DRIVER"] = "offscreen";
        info.Environment.Remove("WAYLAND_DISPLAY");
        info.Environment.Remove("DISPLAY");
        Process = Process.Start(info)!;
        Process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                _lines.Add(line);
            }
        };
        Process.ErrorDataReceived += (_, _) => { };
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
        var socket = WaitFor("SOCKET ", TimeSpan.FromSeconds(15));
        Socket = socket["SOCKET ".Length..];
    }

    public Process Process { get; }

    public string RuntimeDir { get; }

    public string Socket { get; }

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

    public static string? Which(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':'))
        {
            var candidate = Path.Combine(directory, name);
            if (directory.Length > 0 && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string WaitFor(string prefix, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_lines.TryTake(out var line, TimeSpan.FromMilliseconds(100)) && line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line;
            }

            if (Process.HasExited && _lines.Count == 0)
            {
                break;
            }
        }

        throw new TimeoutException($"sdl-comp never printed '{prefix}'");
    }

    public void Send(string line)
    {
        Process.StandardInput.WriteLine(line);
        Process.StandardInput.Flush();
    }

    public Process StartClient(string path, params string[] arguments)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["XDG_RUNTIME_DIR"] = RuntimeDir;
        info.Environment["WAYLAND_DISPLAY"] = Socket;
        var client = Process.Start(info)!;
        client.OutputDataReceived += (_, _) => { };
        client.ErrorDataReceived += (_, _) => { };
        client.BeginOutputReadLine();
        client.BeginErrorReadLine();
        _clients.Add(client);
        return client;
    }

    public Process StartWaypipe(params string[] arguments)
    {
        var info = new ProcessStartInfo(Which("waypipe")!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["XDG_RUNTIME_DIR"] = RuntimeDir;
        info.Environment.Remove("WAYLAND_DISPLAY");
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _clients.Add(process);
        return process;
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            if (!client.HasExited)
            {
                client.Kill();
            }

            client.WaitForExit(2000);
            client.Dispose();
        }

        if (!Process.HasExited)
        {
            try
            {
                Send("quit");
            }
            catch (IOException)
            {
            }

            if (!Process.WaitForExit(5000))
            {
                Process.Kill();
            }
        }

        Process.Dispose();
        try
        {
            Directory.Delete(RuntimeDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
