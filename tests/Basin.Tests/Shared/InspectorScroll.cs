using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Basin.Tests;

internal static partial class InspectorScroll
{
    private const int PanelLeft = 24;
    private const int PanelRight = 544;
    private const int PanelBelowTitle = 80;
    private const int PanelBottom = 400;

    public static string? Client(string name)
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
                    var info = new ProcessStartInfo("make") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = clients };
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

    public static async Task<(int Before, int OverPanel, int Overlay, int After, string Where)> Run(
        string display,
        Func<string, Func<string, bool>, Task<string?>> ask,
        Func<string, Task> send,
        CancellationToken cancel)
    {
        var ssdwin = Client("ssdwin") ?? throw new InvalidOperationException("scripts/wlclients/bin/ssdwin cannot be built");
        var vptr = Client("vptr") ?? throw new InvalidOperationException("scripts/wlclients/bin/vptr cannot be built");
        using var window = new ClientLines(ssdwin, display, "client");

        Match? geometry = null;
        for (var attempt = 0; attempt < 40 && geometry is null; attempt++)
        {
            var list = await ask("ipc windows/list {}", line => line.StartsWith("OK windows/list", StringComparison.Ordinal));
            var match = SsdwinGeometry().Match(list ?? string.Empty);
            if (match.Success && Int(match, 3) > 0)
            {
                geometry = match;
            }
            else
            {
                await Task.Delay(250, cancel);
            }
        }

        if (geometry is null)
        {
            throw new InvalidOperationException("windows/list never gave ssdwin a geometry");
        }

        var outputs = await ask("ipc outputs/list {}", line => line.StartsWith("OK outputs/list", StringComparison.Ordinal));
        var mode = OutputMode().Match(outputs ?? string.Empty);
        var (width, height) = (Int(mode, 1), Int(mode, 2));
        var (x, y, w, h) = (Int(geometry, 1), Int(geometry, 2), Int(geometry, 3), Int(geometry, 4));
        var left = Math.Max(x, PanelLeft);
        var right = Math.Min(x + w, PanelRight);
        var top = Math.Max(y, PanelBelowTitle);
        var bottom = Math.Min(y + h, PanelBottom);
        if (right <= left || bottom <= top)
        {
            throw new InvalidOperationException($"ssdwin at {x},{y} {w}x{h} does not lie under the Outputs panel");
        }

        var (px, py) = ((left + right) / 2, (top + bottom) / 2);
        var where = string.Create(CultureInfo.InvariantCulture, $"ssdwin {x},{y} {w}x{h}, scroll at {px},{py} on {width}x{height}");

        async Task<int> Scroll()
        {
            var mark = window.Count;
            using (var scroller = new ClientLines(vptr, display, "--scroll", $"{px}", $"{py}", $"{width}", $"{height}", "3"))
            {
                scroller.WaitForExit();
            }

            await Task.Delay(600, cancel);
            return window.CountSince(mark, line => line.StartsWith("AXIS ", StringComparison.Ordinal));
        }

        var before = await Scroll();
        await send("inspector open");
        await Task.Delay(1200, cancel);
        await send("inspector-panel outputs off");
        await Task.Delay(600, cancel);
        await send("inspector-panel outputs on");
        await Task.Delay(1200, cancel);
        var overPanel = await Scroll();
        var taken = await ask("inspector-input", line => line.StartsWith("INSPECTOR-INPUT ", StringComparison.Ordinal));
        var scrolls = ScrollCount().Match(taken ?? string.Empty);
        var overlay = scrolls.Success ? Int(scrolls, 1) : -1;
        await send("inspector close");
        await Task.Delay(800, cancel);
        var after = await Scroll();
        return (before, overPanel, overlay, after, where);
    }

    private static int Int(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex("\"title\":\"ssdwin\".*?\"client_geometry\":\\{\"x\":(-?\\d+),\"y\":(-?\\d+),\"width\":(\\d+),\"height\":(\\d+)\\}")]
    private static partial Regex SsdwinGeometry();

    [GeneratedRegex("scrolls=(\\d+)")]
    private static partial Regex ScrollCount();

    [GeneratedRegex("\"mode\":\\{\"width\":(\\d+),\"height\":(\\d+)")]
    private static partial Regex OutputMode();

    private sealed class ClientLines : IDisposable
    {
        private readonly Process _process;
        private readonly List<string> _lines = [];

        public ClientLines(string path, string display, params string[] arguments)
        {
            var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["WAYLAND_DISPLAY"] = display;
            _process = Process.Start(info)!;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lock (_lines)
                    {
                        _lines.Add(line);
                    }
                }
            };
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public int Count
        {
            get
            {
                lock (_lines)
                {
                    return _lines.Count;
                }
            }
        }

        public int CountSince(int mark, Func<string, bool> match)
        {
            lock (_lines)
            {
                return _lines.Skip(mark).Count(match);
            }
        }

        public void WaitForExit() => _process.WaitForExit(20_000);

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }

            _process.Dispose();
        }
    }
}
