using System.Text;
using Basin;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Pty;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private const int TerminalCascade = 20;

    private readonly Dictionary<ShellWindow, TerminalWindow> _terminals = [];
    private AppGroup? _terminalApp;
    private int _terminalCount;

    internal IReadOnlyDictionary<ShellWindow, TerminalWindow> Terminals => _terminals;

    private AppGroup TerminalApp
    {
        get
        {
            if (_terminalApp is null)
            {
                _terminalApp = new AppGroup("terminal", "terminal", 0, "Terminal")
                {
                    Icon = IconFor("utilities-terminal", "application"),
                };
                BuildTerminalMenus(_terminalApp);
            }

            return _terminalApp;
        }
    }

    internal string TerminalShell() =>
        _config.TerminalShell is { Length: > 0 } shell ? shell
        : Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } login ? login
        : "/bin/sh";

    internal string[]? TerminalCommand()
    {
        if (_config.TerminalCommand is { Length: > 0 } command && ExecLine.Split(command) is { Length: > 0 } argv)
        {
            return [.. argv, "-e"];
        }

        var terminal = Environment.GetEnvironmentVariable("TERMINAL");
        return !string.IsNullOrWhiteSpace(terminal) && ExecLine.Split(terminal) is { Length: > 0 } named ? [.. named, "-e"] : null;
    }

    internal void NewTerminal()
    {
        if (_config.TerminalCommand is { Length: > 0 } command && ExecLine.Split(command) is { Length: > 0 } argv)
        {
            Spawn(ExecLine.Join(argv));
            return;
        }

        OpenTerminal(null);
    }

    internal ShellWindow OpenTerminal(IReadOnlyList<string>? argv, string? title = null, string? directory = null)
    {
        var state = TerminalState.Load();
        var fontSize = state.FontSize ?? _config.TerminalFontSize;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var start = new PtyStartInfo(argv is { Count: > 0 } ? argv : [TerminalShell()])
        {
            WorkingDirectory = directory is { Length: > 0 } && Directory.Exists(directory) ? directory : Directory.Exists(home) ? home : null,
            Environment = TerminalEnvironment(),
        };
        var terminal = new TerminalWindow(_host.Loop, start, title ?? "Terminal", fontSize, _config.TerminalScrollback, _config.Depth);
        var (cellWidth, cellHeight) = fontSize == 12 ? (7, 16) : (6, 11);
        var (width, height) = state.Window is { } saved
            ? (Math.Max(120, saved.Width), Math.Max(80, saved.Height))
            : ((80 * cellWidth) + (2 * TerminalWindow.Border) + TerminalWindow.BarSize, (24 * cellHeight) + TerminalWindow.Border + TerminalWindow.BarSize);
        (int X, int Y)? placement = null;
        if (state.Window is { } box)
        {
            var open = _terminals.Count(t => t.Key.Mapped);
            placement = ClampToWork(box.X + (open * TerminalCascade), box.Y + (open * TerminalCascade), width, height);
        }

        var id = ++_terminalCount;
        var window = OpenShellWindow($"terminal:{id}", terminal.Title, terminal.View, width, height,
            resizable: true, scrollBars: true, placement: placement, app: TerminalApp);
        _terminals[window] = terminal;
        terminal.Started += () =>
        {
            var (columns, rows) = terminal.Cells;
            BasinReport.Line($"TERMINAL + {window.Id} pid={terminal.Session!.ProcessId} cols={columns} rows={rows}");
        };
        terminal.FailedToStart += failure => BasinReport.Line($"TERMINAL failed {window.Id} {failure}");
        terminal.TitleChanged += text =>
        {
            if (window.Mapped)
            {
                RetitleShellWindow(window, text);
                BasinReport.Line($"TERMINAL title {window.Id} \"{text}\"");
            }
        };
        terminal.Bell += Beep;
        terminal.Exited += exit => OnTerminalExited(window, terminal, exit);
        window.ShellPlaced = () => SaveTerminalBox(window);
        window.ShellClosed = () =>
        {
            _terminals.Remove(window);
            terminal.Dispose();
            BasinReport.Line($"TERMINAL - {window.Id}");
        };
        return window;
    }

    private (int X, int Y) ClampToWork(int x, int y, int width, int height)
    {
        var view = _outputs.Views.FirstOrDefault(v => _layout.BoxOf(v.Output) is var b && x >= b.X && y >= b.Y && x < b.Right && y < b.Bottom)
            ?? MainView ?? _outputs.Views[0];
        var area = WorkArea(view);
        var clampedX = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - Math.Min(width, area.Width)));
        var clampedY = Math.Clamp(y, area.Y + ClientFrame.TitleHeight, Math.Max(area.Y + ClientFrame.TitleHeight, area.Bottom - Math.Min(height, area.Height)));
        return (clampedX, clampedY);
    }

    private Dictionary<string, string?> TerminalEnvironment()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = null,
            ["TERM_PROGRAM"] = "system7-wm",
            ["WAYLAND_DISPLAY"] = _host.Socket,
            ["XDG_SESSION_TYPE"] = "wayland",
            ["DISPLAY"] = _xServer?.DisplayName is { Length: > 0 } display ? display : null,
        };
        if (_xwaylandModule is { } module)
        {
            foreach (var (name, value) in module.ChildEnvironment())
            {
                values[name] = value;
            }
        }

        return values;
    }

    private void OnTerminalExited(ShellWindow window, TerminalWindow terminal, PtyExit exit)
    {
        var what = exit.Signal is { } signal ? $"signal={TerminalSignals.Name(signal)}" : $"status={exit.Status}";
        if (exit.Succeeded)
        {
            BasinReport.Line($"TERMINAL exit {window.Id} {what} closed");
            if (window.Mapped)
            {
                CloseShellWindow(window);
            }

            return;
        }

        terminal.ShowExit(exit);
        BasinReport.Line($"TERMINAL exit {window.Id} {what} kept");
    }

    private void SaveTerminalBox(ShellWindow window)
    {
        if (!window.Mapped || window.Zoomed || !_terminals.TryGetValue(window, out var terminal))
        {
            return;
        }

        var state = TerminalState.Load();
        state.Window = new Box(window.X, window.Y, window.ContentDip.Width, window.ContentDip.Height);
        state.FontSize ??= terminal.FontSize;
        try
        {
            state.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"cannot save the terminal window to {TerminalState.FileOf()}: {error.Message}");
        }
    }

    private void SaveTerminalFont(int size)
    {
        var state = TerminalState.Load();
        state.FontSize = size;
        try
        {
            state.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"cannot save the terminal font to {TerminalState.FileOf()}: {error.Message}");
        }
    }

    private (ShellWindow Window, TerminalWindow Terminal)? FrontTerminal()
    {
        if (_active is { } active && _terminals.TryGetValue(active, out var terminal))
        {
            return (active, terminal);
        }

        var last = TerminalApp.Windows.LastOrDefault(w => _terminals.ContainsKey(w));
        return last is null ? null : (last, _terminals[last]);
    }

    private void BuildTerminalMenus(AppGroup app)
    {
        var menu = app.Synthesized;
        menu.Clear();
        var fonts = TerminalFonts.Sizes
            .Select(size => (Size: size, Item: menu.Item($"Monaco {size}", () => SetTerminalFont(size), check: false)))
            .ToList();
        menu.Set(
            menu.Submenu("File",
            [
                menu.Item("New Window", () => OpenTerminal(null), 'N'),
                menu.Item("Close Window", () => FrontTerminal()?.Window.Close(), 'W'),
            ]),
            menu.Submenu("Edit",
            [
                menu.Item("Copy", () => _ = FrontTerminal()?.Terminal.Control.CopySelectionAsync(), 'C'),
                menu.Item("Paste", () => _ = FrontTerminal()?.Terminal.Control.PasteFromClipboardAsync(), 'V'),
                menu.Item("Select All", () => FrontTerminal()?.Terminal.Control.SelectAll(), 'A'),
                menu.Separator(),
                menu.Item("Clear Scrollback", () => FrontTerminal()?.Terminal.ClearScrollback(), 'K'),
            ]),
            menu.Submenu("Font", fonts.Select(f => f.Item), opening: () =>
            {
                var current = FrontTerminal()?.Terminal.FontSize ?? _config.TerminalFontSize;
                var changed = false;
                foreach (var (size, item) in fonts)
                {
                    changed |= item.ToggleState != (size == current);
                    item.ToggleState = size == current;
                }

                if (changed)
                {
                    menu.Menu.NotifyChanged();
                }
            }));
    }

    private void SetTerminalFont(int size)
    {
        if (FrontTerminal() is not { } front)
        {
            return;
        }

        front.Terminal.SetFontSize(size);
        SaveTerminalFont(size);
        BasinReport.Line($"TERMINAL font {front.Window.Id} {size}");
    }

    private void ApplyTerminalDepth()
    {
        foreach (var terminal in _terminals.Values)
        {
            terminal.SetDepth(_config.Depth);
        }
    }

    private bool TryTerminalChord(uint key)
    {
        if (_config.NewTerminal is not { } chord || HeldModifiers != chord.ModifierMask)
        {
            return false;
        }

        var symbol = Seat.Keyboard.RawKeysymFor(key).Value;
        if (Lower(symbol) != Lower(chord.Keysym))
        {
            return false;
        }

        BasinReport.Line("CHORD new_terminal");
        NewTerminal();
        return true;
    }

    private void RegisterTerminalCommands(Basin.Ipc.IpcMethodRegistry methods)
    {
        _report.Register(methods, "system7wm/terminal", "terminal [{argv...}]", (ref Basin.Ipc.IpcParams p, Basin.Ipc.IpcReply _) =>
        {
            var text = p.Has("argv") ? p.GetString("argv") : "";
            if (p.Failed)
            {
                return;
            }

            var argv = ShellWords.Split(text);
            var window = OpenTerminal(argv.Length > 0 ? argv : null);
            _report.Line($"TERMINAL open {window.Id}");
        });

        _report.Register(methods, "system7wm/terminal-send", "terminal-send {id:int} {text...}", (ref Basin.Ipc.IpcParams p, Basin.Ipc.IpcReply _) =>
        {
            var id = (int)p.GetInt("id");
            var text = p.GetString("text");
            if (p.Failed)
            {
                return;
            }

            if (_terminals.FirstOrDefault(t => t.Key.Id == id) is { Key: not null } found)
            {
                found.Value.Send(TerminalText(text));
                _report.Line($"TERMINAL sent {id}");
            }
            else
            {
                _report.Line($"TERMINAL missing {id}");
            }
        });

        _report.Register(methods, "system7wm/terminal-close", "terminal-close {id:int}", (ref Basin.Ipc.IpcParams p, Basin.Ipc.IpcReply _) =>
        {
            var id = (int)p.GetInt("id");
            if (p.Failed)
            {
                return;
            }

            if (_terminals.Keys.FirstOrDefault(w => w.Id == id) is { } window)
            {
                CloseShellWindow(window);
            }
            else
            {
                _report.Line($"TERMINAL missing {id}");
            }
        });
    }

    private static uint Lower(uint keysym) => keysym is >= 'A' and <= 'Z' ? keysym + 32 : keysym;

    private void DisposeTerminals()
    {
        foreach (var terminal in _terminals.Values)
        {
            terminal.Dispose();
        }

        _terminals.Clear();
        PtySession.CollectOrphans(TimeSpan.FromMilliseconds(500));
    }

    private static byte[] TerminalText(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var next = text[++i];
                builder.Append(next switch
                {
                    'r' => '\r',
                    'n' => '\n',
                    't' => '\t',
                    'e' => '\u001b',
                    '\\' => '\\',
                    _ => next,
                });
                continue;
            }

            builder.Append(text[i]);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
