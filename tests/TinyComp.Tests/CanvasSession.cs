using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Basin.Effects;
using Basin;
using Xunit;
using static TinyComp.Tests.CanvasRows;

namespace TinyComp.Tests;

internal sealed class CanvasSession : IDisposable
{
    private readonly Process _compositor;
    private readonly string _runtimeDir;
    private readonly List<string> _lines = [];
    private readonly List<string> _clientLines = [];
    private readonly List<string> _errorLines = [];
    private readonly List<Process> _extra = [];
    private string _socket = string.Empty;
    private string? _options;
    private readonly object _gate = new();
    private Process? _client;

    private CanvasSession(Process compositor, string runtimeDir)
    {
        _compositor = compositor;
        _runtimeDir = runtimeDir;
    }

    public static CanvasSession? Start(
        string config = "[canvas]\nenable = true\ngrid = \"never\"\nanimation_ms = 100\n",
        string? clientPath = "weston-simple-shm",
        string[]? clientArguments = null,
        string[]? compositorArguments = null)
    {
        if (!OperatingSystem.IsLinux() || Locate("tinycomp") is not { } compositorPath || clientPath is null ||
            (clientPath == "weston-simple-shm" && !ClientAvailable()))
        {
            return null;
        }

        var tag = Guid.NewGuid().ToString("N")[..12];
        var runtimeDir = Path.Combine("/tmp", $"basin-canvas-{tag}");
        Directory.CreateDirectory(runtimeDir);
        File.SetUnixFileMode(runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var configPath = Path.Combine(runtimeDir, "tinycomp.toml");
        File.WriteAllText(configPath, config);

        var info = new ProcessStartInfo(compositorPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--backend");
        info.ArgumentList.Add("headless");
        info.ArgumentList.Add("--renderer");
        info.ArgumentList.Add("pixman");
        info.ArgumentList.Add("--config");
        info.ArgumentList.Add(configPath);
        foreach (var argument in compositorArguments ?? [])
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
        info.Environment.Remove("WAYLAND_DISPLAY");
        var compositor = Process.Start(info)!;
        var session = new CanvasSession(compositor, runtimeDir);
        compositor.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                lock (session._gate)
                {
                    session._lines.Add(line);
                    if (line.StartsWith("OPTIONS ", StringComparison.Ordinal))
                    {
                        session._options = line;
                    }

                    Monitor.PulseAll(session._gate);
                }
            }
        };
        compositor.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                lock (session._gate)
                {
                    session._errorLines.Add(line);
                }
            }
        };
        compositor.BeginOutputReadLine();
        compositor.BeginErrorReadLine();

        var socketLine = session.WaitForLine("SOCKET ");
        if (socketLine is null)
        {
            session.Dispose();
            return null;
        }

        var clientInfo = new ProcessStartInfo(clientPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in clientArguments ?? [])
        {
            clientInfo.ArgumentList.Add(argument);
        }

        clientInfo.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
        session._socket = socketLine.Split(' ')[1];
        clientInfo.Environment["WAYLAND_DISPLAY"] = session._socket;
        var client = Process.Start(clientInfo)!;
        client.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                lock (session._gate)
                {
                    session._clientLines.Add(line);
                    Monitor.PulseAll(session._gate);
                }
            }
        };
        client.ErrorDataReceived += (_, _) => { };
        client.BeginOutputReadLine();
        client.BeginErrorReadLine();
        session._client = client;

        if (session.WaitForLine("MAPPED ") is null)
        {
            session.Dispose();
            return null;
        }

        return session;
    }

    public void Spawn(string path, params string[] arguments)
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

        info.Environment["XDG_RUNTIME_DIR"] = _runtimeDir;
        info.Environment["WAYLAND_DISPLAY"] = _socket;
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                lock (_gate)
                {
                    _clientLines.Add(line);
                    Monitor.PulseAll(_gate);
                }
            }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _extra.Add(process);
    }

    public string SocketPath => Path.Combine(_runtimeDir, _socket);

    public void Send(string command)
    {
        _compositor.StandardInput.WriteLine(command);
        _compositor.StandardInput.Flush();
        Thread.Sleep(60);
    }

    public void SendPaced(IEnumerable<string> commands, int gapMillis)
    {
        foreach (var command in commands)
        {
            _compositor.StandardInput.WriteLine(command);
            _compositor.StandardInput.Flush();
            Thread.Sleep(gapMillis);
        }

        Thread.Sleep(60);
    }

    public string? WaitForLine(string prefix, int timeoutMillis = 15000) => WaitIn(_lines, prefix, timeoutMillis);

    public string? WaitForClientLine(string prefix, int timeoutMillis = 15000) => WaitIn(_clientLines, prefix, timeoutMillis);

    private string? WaitIn(List<string> lines, string prefix, int timeoutMillis)
    {
        var deadline = Environment.TickCount64 + timeoutMillis;
        lock (_gate)
        {
            var from = 0;
            while (true)
            {
                for (; from < lines.Count; from++)
                {
                    if (lines[from].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        var found = lines[from];
                        lines.RemoveRange(0, from + 1);
                        return found;
                    }
                }

                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0 || _compositor.HasExited)
                {
                    return null;
                }

                Monitor.Wait(_gate, (int)Math.Min(remaining, 500));
            }
        }
    }

    public (int X, int Y) WindowPosition()
    {
        Send("where");
        var line = WaitForLine("WIN ");
        Assert.NotNull(line);
        var parts = line!.Split(' ');
        return (int.Parse(parts[2]), int.Parse(parts[3]));
    }

    public (int X, int Y) WaitForWindowX(int x, int timeoutMillis = 10000)
    {
        var deadline = Environment.TickCount64 + timeoutMillis;
        var position = WindowPosition();
        while (position.X != x && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(50);
            position = WindowPosition();
        }

        return position;
    }

    public void Rewrite(string config) => File.WriteAllText(Path.Combine(_runtimeDir, "tinycomp.toml"), config);

    public string RuntimeDir => _runtimeDir;

    public string[] ErrorLines()
    {
        lock (_gate)
        {
            return [.. _errorLines];
        }
    }

    public double CanvasScale()
    {
        Send("where");
        var line = WaitForLine("CANVASWIN ");
        Assert.NotNull(line);
        var at = line!.IndexOf(" scale=", StringComparison.Ordinal);
        Assert.True(at > 0, line);
        var end = line.IndexOf(' ', at + 1);
        return double.Parse(line[(at + 7)..(end < 0 ? line.Length : end)], CultureInfo.InvariantCulture);
    }

    public Dictionary<string, string> CanvasFields()
    {
        Send("where");
        var line = WaitForLine("CANVASWIN ");
        Assert.NotNull(line);
        var fields = new Dictionary<string, string>();
        foreach (var part in line!.Split(' '))
        {
            var equals = part.IndexOf('=');
            if (equals > 0)
            {
                fields[part[..equals]] = part[(equals + 1)..];
            }
        }

        return fields;
    }

    public (int X, int Y, Box Screen, bool Deformed, string Home) CanvasWindow()
    {
        Send("where");
        var line = WaitForLine("CANVASWIN ");
        Assert.NotNull(line);
        var fields = new Dictionary<string, string>();
        foreach (var part in line!.Split(' '))
        {
            var equals = part.IndexOf('=');
            if (equals > 0)
            {
                fields[part[..equals]] = part[(equals + 1)..];
            }
        }

        var canvas = fields["canvas"].Split(',');
        var screen = fields["screen"].Split(',');
        return (
            int.Parse(canvas[0], CultureInfo.InvariantCulture),
            int.Parse(canvas[1], CultureInfo.InvariantCulture),
            new Box(
                int.Parse(screen[0], CultureInfo.InvariantCulture),
                int.Parse(screen[1], CultureInfo.InvariantCulture),
                int.Parse(fields["width"], CultureInfo.InvariantCulture),
                int.Parse(fields["height"], CultureInfo.InvariantCulture)),
            fields["deformed"] == "yes",
            fields["home"]);
    }

    public string? Options
    {
        get
        {
            lock (_gate)
            {
                return _options;
            }
        }
    }

    public Shot Shot(string name, int output = 0)
    {
        Thread.Sleep(400);
        var path = Path.Combine(_runtimeDir, $"{name}.png");
        Send(output == 0 ? $"shotraw {path}" : $"shotraw {path} {output}");
        var line = WaitForLine("SHOTRAW ");
        Assert.True(line is not null && line.StartsWith($"SHOTRAW {path}", StringComparison.Ordinal), line ?? "no SHOTRAW line");
        var (rgba, width, height) = PngCodec.Decode(File.ReadAllBytes(path));
        return new Shot(rgba, width, height);
    }

    public void Dispose()
    {
        try
        {
            if (!_compositor.HasExited)
            {
                _compositor.StandardInput.WriteLine("quit");
                _compositor.StandardInput.Flush();
                _ = _compositor.WaitForExit(3000);
            }
        }
        catch (IOException)
        {
        }

        if (_client is { } client && !client.HasExited)
        {
            client.Kill(entireProcessTree: true);
        }

        foreach (var extra in _extra)
        {
            if (!extra.HasExited)
            {
                extra.Kill(entireProcessTree: true);
            }

            extra.Dispose();
        }

        if (!_compositor.HasExited)
        {
            _compositor.Kill(entireProcessTree: true);
        }

        _compositor.Dispose();
        _client?.Dispose();
        try
        {
            Directory.Delete(_runtimeDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static bool ClientAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(directory, "weston-simple-shm")))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Locate(string name)
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
}
