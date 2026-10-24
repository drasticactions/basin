using Basin.Cli;
using Basin.Host;
using Basin.Diagnostics;
using Basin.Ipc;

namespace TabWm;

internal sealed partial class TabWm
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;
    private FrameSettle? _settle;

    private void WireIpc()
    {
        _settle = new FrameSettle(_outputs, _uiDriver);
        _ipc = _options.Ipc.Attach(_host.Loop, _services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "tab-wm",
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = _options.Renderer,
            XwaylandDisplay = () => _xServer?.DisplayName,
            Quit = Stop,
            Settle = _settle,
            Processes = _processes,
        });
        _ipc.SyntheticInput = _seat?.Injector;
        _ipc.GuardWithCurtain(_curtain);
        RegisterCommands(_ipc.Methods);
        _ipc.Start();
        _ipc.StartLineFront();
    }

    private void RegisterCommands(IpcMethodRegistry methods)
    {
        _report.AddInputLines(methods);
        _report.AddSessionLines(methods);
        _report.Register(methods, "tabwm/tabs", "tabs", PrintTabs);
        _report.Register(methods, "tabwm/tab-new", "tab new [{output:int}]", (ref IpcParams p, IpcReply _) =>
        {
            var index = p.TryGetInt("output", out var output) ? (int)output : -1;
            var set = index >= 0 && index < _sets.Count ? _sets[index] : FocusedSet;
            if (set is not null)
            {
                _report.Line(Describe(NewTab(set, select: true)));
            }
        });
        _report.Register(methods, "tabwm/tab-select", "tab select {id:int}", (ref IpcParams p, IpcReply _) =>
        {
            if (TabArgument(ref p, "id") is { } tab)
            {
                SelectTab(tab);
                _report.Line(Describe(tab));
            }
        });
        _report.Register(methods, "tabwm/tab-close", "tab close {id:int}", (ref IpcParams p, IpcReply _) =>
        {
            if (TabArgument(ref p, "id") is { } tab)
            {
                CloseTab(tab);
            }
        });
        _report.Register(methods, "tabwm/tab-keep", "tab keep {id:int}", (ref IpcParams p, IpcReply _) =>
        {
            if (TabArgument(ref p, "id") is { } tab)
            {
                KeepTab(tab);
            }
        });
        _report.Register(methods, "tabwm/tab-move", "tab move {id:int} {output:int} {index:int}", (ref IpcParams p, IpcReply _) =>
        {
            var tab = TabArgument(ref p, "id");
            var output = (int)p.GetInt("output");
            var index = (int)p.GetInt("index");
            if (tab is not null && !p.Failed && output >= 0 && output < _sets.Count)
            {
                MoveTab(tab, _sets[output], index, select: true);
                _report.Line(Describe(tab));
            }
        });
        _report.Register(methods, "tabwm/tab-rename", "tab rename {id:int} [{label...}]", (ref IpcParams p, IpcReply _) =>
        {
            if (TabArgument(ref p, "id") is { } tab)
            {
                RenameTab(tab, p.TryGetString("label", out var label) && label.Length > 0 ? label : null);
                _report.Line(Describe(tab));
            }
        });
        _report.Register(methods, "tabwm/tab-mute", "tab mute {id:int} {state:on|off}", (ref IpcParams p, IpcReply _) =>
        {
            var tab = TabArgument(ref p, "id");
            var state = p.GetString("state");
            if (tab is not null && !p.Failed)
            {
                SetMuted(tab, state == "on");
                _report.Line(Describe(tab));
            }
        });
        _report.Register(methods, "tabwm/launch", "launch {app} [{tab:int}]", (ref IpcParams p, IpcReply _) =>
        {
            var app = p.GetString("app");
            var tab = p.TryGetInt("tab", out var id) ? FindTab((ulong)id) : null;
            if (!p.Failed)
            {
                LaunchId(app, tab ?? FocusedSet?.Active);
            }
        });
        _report.Register(methods, "tabwm/exec", "exec {command...}", (ref IpcParams p, IpcReply _) =>
        {
            var command = p.GetString("command");
            if (!p.Failed)
            {
                Launch(FocusedSet?.Active, command, null, command);
            }
        });
        _report.Register(methods, "tabwm/window-tab", "window tab {window:int} {tab:int}", (ref IpcParams p, IpcReply _) =>
        {
            var window = FindWindow((ulong)p.GetInt("window"));
            var tab = TabArgument(ref p, "tab");
            if (window is not null && tab is not null)
            {
                MoveWindow(window, tab, follow: false);
            }
        });
        _report.Register(methods, "tabwm/windows", "windows", PrintWindows);
        _report.Register(methods, "tabwm/launcher", "launcher {state:show|hide}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (p.Failed)
            {
                return;
            }

            if (state == "show" && FocusedSet is { } set)
            {
                ShowOverlay(set);
            }
            else
            {
                CloseOverlay();
            }
        });
        _report.Register(methods, "tabwm/settings", "settings {state:show|hide} [{section:general|keys|about}]", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (p.Failed)
            {
                return;
            }

            var section = p.TryGetString("section", out var name) ? name : "general";
            if (state == "show" && PrimaryOrFocusedSet() is { } set)
            {
                ShowSettings(set, section switch { "keys" => SettingsKeys, "about" => SettingsAbout, _ => SettingsGeneral });
            }
            else if (Settings is not null)
            {
                CloseOverlay();
            }
        });
        _report.Register(methods, "tabwm/launcher-search", "search {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed && (_overlay?.Model ?? PageModel()) is { } model)
            {
                model.SearchText = text;
                foreach (var result in model.Results)
                {
                    _report.Line($"RESULT {result.Id} \"{result.Name}\"");
                }
            }
        });
        _report.Register(methods, "tabwm/session-save", "session save", () => SaveSession("command"));
        _report.Register(methods, "tabwm/clock", "clock {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed && _clock is { } clock)
            {
                clock.Frozen = text == "live" ? null : text;
                _report.Line($"CLOCK {text}");
            }
        });
        _report.Register(methods, "tabwm/framecount", "framecount", () =>
            _report.Line($"FRAMECOUNT {_outputs.Views.Sum(view => view.Rendered)}"));
        _report.Register(methods, "tabwm/glib", "glib", () =>
            _report.Line(_glib is { } glib
                ? $"GLIB armed={(glib.TimerArmed ? "yes" : "no")} fds={glib.WatchedDescriptors} dispatches={glib.Dispatches}"
                : "GLIB none"));
        _report.Register(methods, "tabwm/shot", "shot {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                WriteScreenshot(path);
                _report.Line($"SHOT {path}");
            }
        });
        _report.Register(methods, "tabwm/shotraw", "shotraw {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                var buffer = _outputs.Views.FirstOrDefault()?.LastPresentedBuffer;
                _report.Line(CompositorLines.ShotRaw(Basin.Scene.SceneScreenshot.WritePresented(buffer, _renderer, path), buffer, path));
            }
        });
        _report.Register(methods, "tabwm/pointer", "where", () =>
            _report.Line($"POINTER {_seat?.PointerX ?? 0} {_seat?.PointerY ?? 0} keyboard={(KeyboardTarget is null ? "client" : "chrome")}"));
    }

    private Chrome.LauncherViewModel? PageModel() =>
        FocusedSet is { } set && _pages.TryGetValue(set, out var page) ? page.Model : null;

    private Tab? TabArgument(ref IpcParams p, string name)
    {
        var id = p.GetInt(name);
        if (p.Failed)
        {
            return null;
        }

        if (FindTab((ulong)id) is { } tab)
        {
            return tab;
        }

        _report.Line($"ERR no tab {id}");
        return null;
    }

    private void PrintTabs()
    {
        foreach (var set in _sets)
        {
            foreach (var tab in set.Tabs)
            {
                _report.Line(Describe(tab));
            }
        }

        for (var i = 0; i < _sets.Count; i++)
        {
            var set = _sets[i];
            _report.Line($"STRIP output={i} name={set.Output.Name} hidden={(set.StripHidden ? 1 : 0)} peek={(set.StripPeek ? 1 : 0)} height={set.StripHeight} work={set.Work.X},{set.Work.Y},{set.Work.Width}x{set.Work.Height}");
        }
    }

    private void PrintWindows()
    {
        foreach (var window in _windows)
        {
            if (!window.Mapped)
            {
                continue;
            }

            var geometry = window.Geometry;
            _report.Line($"WINDOW id={window.Id} tab={window.Tab?.Id ?? 0} app={(window.AppId.Length > 0 ? window.AppId : "-")} " +
                $"title=\"{window.Title}\" x={geometry.X} y={geometry.Y} w={geometry.Width} h={geometry.Height} " +
                $"focused={(ReferenceEquals(window, _focused) ? 1 : 0)} maximized={(window.Maximized ? 1 : 0)} fullscreen={(window.Fullscreen ? 1 : 0)} " +
                $"minimized={(window.Minimized ? 1 : 0)} framed={(window.Frame is null ? 0 : 1)} x11={(window.X11 is null ? 0 : 1)} via={TabPlacement.Name(window.Via)}");
        }
    }
}
