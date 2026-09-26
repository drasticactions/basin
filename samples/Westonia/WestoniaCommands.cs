using Basin.Cli;
using Basin.Ipc;
using Basin.Diagnostics;

namespace Westonia;

internal sealed partial class Westonia
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;

    private void WireIpc()
    {
        _ipc = _options.Ipc.Attach(_host.Loop, _services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "westonia",
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = _options.Renderer,
            XwaylandDisplay = () => _xServer?.DisplayName,
            Quit = Stop,
        });
        _ipc.SyntheticInput = _seat?.Injector;
        RegisterCommands(_ipc.Methods);
        _ipc.Start();
        _ipc.StartLineFront();
    }

    private void RegisterCommands(IpcMethodRegistry methods)
    {
        _report.AddLine(methods, IpcMethodNames.InputPointerMove, "move {x:number} {y:number}");
        _report.AddLine(methods, IpcMethodNames.InputPointerButton, "button {button:int} {pressed:bool}");
        _report.AddLine(methods, IpcMethodNames.InputKey, "key {code:int} {pressed:bool}");
        _report.AddLine(methods, IpcMethodNames.SessionQuit, "quit");

        _report.Register(methods, "westonia/shot", "shot {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                WriteScreenshot(path);
            }
        });

        _report.Register(methods, "westonia/shotraw", "shotraw {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                WritePresented(path);
            }
        });

        _report.Register(methods, "westonia/planeshot", "planeshot {prefix}", (ref IpcParams p, IpcReply _) =>
        {
            var prefix = p.GetString("prefix");
            if (!p.Failed)
            {
                WritePlanes(prefix);
            }
        });

        _report.Register(methods, "westonia/where", "where", PrintState);
        _report.Register(methods, "westonia/lock", "lock", () => _lock?.Lock());
        _report.Register(methods, "westonia/unlock", "unlock", () => _lock?.Unlock());
        _report.Register(methods, "westonia/idle", "idle", () =>
        {
            StartScreensaver();
            _lock?.Lock();
        });

        _report.Register(methods, "westonia/theme", "theme {variant}", (ref IpcParams p, IpcReply _) =>
        {
            var variant = p.GetString("variant");
            if (p.Failed)
            {
                return;
            }

            _ui.Theme = variant == "dark"
                ? Basin.UI.Avalonia.UIThemeVariant.Dark
                : Basin.UI.Avalonia.UIThemeVariant.Light;
            _outputs.ScheduleAll();
        });
    }

    private void WritePlanes(string prefix)
    {
        if (_outputs.Views.FirstOrDefault() is not { } view)
        {
            _report.Line($"PLANESHOT {prefix} images=0");
            return;
        }

        var chrome = new List<Basin.Host.PlaneShotChrome>();
        var panels = 0;
        foreach (var elements in _avalonia.Elements.Values)
        {
            chrome.Add(new Basin.Host.PlaneShotChrome($"panel{panels++}", null, elements.PanelSurface.Node.Node));
        }

        chrome.Add(new Basin.Host.PlaneShotChrome("switcher", null, _switcher?.Node));
        _ = Basin.Host.PlaneShot.Write(view, _renderer, prefix, _scene, chrome);
    }

    private void WritePresented(string path)
    {
        var buffer = _outputs.Views.FirstOrDefault()?.LastPresentedBuffer;
        _report.Line(Basin.Scene.SceneScreenshot.WritePresented(buffer, _renderer, path) switch
        {
            Basin.Scene.ScreenshotOutcome.NoFrame => "SHOTRAW none",
            Basin.Scene.ScreenshotOutcome.Unreadable => $"SHOTRAW unreadable {buffer!.Width}x{buffer.Height}",
            _ => $"SHOTRAW {path} {buffer!.Width}x{buffer.Height}",
        });
    }

    private void PrintState()
    {
        _report.Line($"POINTER {_seat?.PointerX ?? 0} {_seat?.PointerY ?? 0}");
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            var work = _avalonia.WorkArea(box.X, box.Y, box.Width, box.Height);
            _report.Line($"AREA {view.Output.Name} output={box} work={work}");
        }

        foreach (var window in _shell.Windows)
        {
            var geometry = window.Geometry;
            _report.Line($"WINDOW \"{window.Window.Title}\" {geometry} ws={window.Workspace + 1} kind={window.Kind} " + $"focused={ReferenceEquals(window, _shell.Focused)} maximized={window.Maximized} " + $"fullscreen={window.Fullscreen} tiled={window.Tiled}");
        }

        _report.Line($"SWITCHER {(_switcher?.IsOpen == true ? "open" : "closed")}");
        _report.Line($"LOCK {(_lock?.IsLocked == true ? "locked" : "unlocked")} " + $"client={(_lock?.ClientLocked == true ? "yes" : "no")} " + $"dialog={(_lock?.Dialog is null ? "none" : "shown")}");
        _report.Line($"SHELLCLIENT backgrounds={_shell.ClientBackgrounds} panels={_shell.ClientPanels} " + $"ready={_shell.DesktopIsReady}");
        _report.Line($"XWINDOWS {_xwayland?.Count ?? 0}");
        _report.Line($"ANIMATING {(_animations?.IsRunning == true ? "yes" : "no")}");
        var hit = _scene.SurfaceAt(_seat?.PointerX ?? 0, _seat?.PointerY ?? 0);
        _report.Line($"HIT scene={(hit?.Surface is null ? "none" : "surface")} " + $"focus={(Seat.Pointer.Focus is null ? "none" : "surface")} " + $"shell={(_seat?.IsOverShell == true ? "yes" : "no")}");
        _report.Line($"GRAB kind={_shell.Grab.Kind} window={(_shell.Grab.Window is null ? "none" : "yes")} " + $"buttons={Seat.Pointer.HasImplicitGrab}");
        foreach (var elements in _avalonia.Elements.Values)
        {
            if (elements.PanelSurface.Surface is { } panel)
            {
                var size = panel.Size;
                _report.Line($"SURFACE panel {size.Width}x{size.Height}@{size.Scale}");
            }
        }

        foreach (var window in _shell.Windows)
        {
            if (window.Frame is { } frame)
            {
                var box = frame.OuterBox;
                _report.Line($"SURFACE frame {box.Width}x{box.Height}@{window.Scale} strips=4");
            }
        }

        _report.Line($"CURSOR {_cursor.Showing} drawn={_cursor.DrawnBy} on={_cursor.CursorOutput?.Name ?? "none"}");
        _report.Line($"POPUPS {_uiDriver.Popups.Count}");
        if (_workspaces is { } workspaces)
        {
            _report.Line($"WORKSPACE {workspaces.Active + 1}/{workspaces.Count} sliding={workspaces.IsSliding} progress={workspaces.SlideProgress:F3}");
        }
    }
}
