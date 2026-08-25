using Basin.Cli;
using Basin.Diagnostics;

namespace Westonia;

internal sealed partial class Westonia
{
    private void WireStdin()
    {
        _stdinCommands = new StdinCommands(_host.Loop, HandleCommand);
        _stdinCommands.CommandFailed += (command, error) =>
        {
            BasinReport.Line(CompositorLines.CommandFailed(command, error));
        };
    }

    private void HandleCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (_seat?.StdinCommands.Handle(parts) == true)
        {
            return;
        }

        switch (parts)
        {
            case ["shot", var path]:
                _uiDriver.Pump();
                WriteScreenshot(path);
                break;
            case ["shotraw", var path]:
                WritePresented(path);
                break;
            case ["where"]:
                PrintState();
                break;
            case ["lock"]:
                _lock?.Lock();
                break;
            case ["unlock"]:
                _lock?.Unlock();
                break;
            case ["idle"]:
                StartScreensaver();
                _lock?.Lock();
                break;
            case ["theme", var variant]:
                _ui.Theme = variant == "dark"
                    ? Basin.UI.Avalonia.UIThemeVariant.Dark
                    : Basin.UI.Avalonia.UIThemeVariant.Light;
                _outputs.ScheduleAll();
                break;
            case ["quit"]:
                Stop();
                break;
        }
    }

    private void WritePresented(string path)
    {
        var buffer = _outputs.Views.FirstOrDefault()?.LastPresentedBuffer;
        BasinReport.Line(Basin.Scene.SceneScreenshot.WritePresented(buffer, _renderer, path) switch
        {
            Basin.Scene.ScreenshotOutcome.NoFrame => "SHOTRAW none",
            Basin.Scene.ScreenshotOutcome.Unreadable => $"SHOTRAW unreadable {buffer!.Width}x{buffer.Height}",
            _ => $"SHOTRAW {path} {buffer!.Width}x{buffer.Height}",
        });
    }

    private void PrintState()
    {
        BasinReport.Line($"POINTER {_seat?.PointerX ?? 0} {_seat?.PointerY ?? 0}");
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            var work = _avalonia.WorkArea(box.X, box.Y, box.Width, box.Height);
            BasinReport.Line($"AREA {view.Output.Name} output={box} work={work}");
        }

        foreach (var window in _shell.Windows)
        {
            var geometry = window.Geometry;
            BasinReport.Line($"WINDOW \"{window.Window.Title}\" {geometry} ws={window.Workspace + 1} kind={window.Kind} " + $"focused={ReferenceEquals(window, _shell.Focused)} maximized={window.Maximized} " + $"fullscreen={window.Fullscreen} tiled={window.Tiled}");
        }

        BasinReport.Line($"SWITCHER {(_switcher?.IsOpen == true ? "open" : "closed")}");
        BasinReport.Line($"LOCK {(_lock?.IsLocked == true ? "locked" : "unlocked")} " + $"client={(_lock?.ClientLocked == true ? "yes" : "no")} " + $"dialog={(_lock?.Dialog is null ? "none" : "shown")}");
        BasinReport.Line($"SHELLCLIENT backgrounds={_shell.ClientBackgrounds} panels={_shell.ClientPanels} " + $"ready={_shell.DesktopIsReady}");
        BasinReport.Line($"XWINDOWS {_xwayland?.Count ?? 0}");
        BasinReport.Line($"ANIMATING {(_animations?.IsRunning == true ? "yes" : "no")}");
        var hit = _scene.SurfaceAt(_seat?.PointerX ?? 0, _seat?.PointerY ?? 0);
        BasinReport.Line($"HIT scene={(hit?.Surface is null ? "none" : "surface")} " + $"focus={(Seat.Pointer.Focus is null ? "none" : "surface")} " + $"shell={(_seat?.IsOverShell == true ? "yes" : "no")}");
        BasinReport.Line($"GRAB kind={_shell.Grab.Kind} window={(_shell.Grab.Window is null ? "none" : "yes")} " + $"buttons={Seat.Pointer.HasImplicitGrab}");
        foreach (var elements in _avalonia.Elements.Values)
        {
            if (elements.PanelSurface.Surface is { } panel)
            {
                var size = panel.Size;
                BasinReport.Line($"SURFACE panel {size.Width}x{size.Height}@{size.Scale}");
            }
        }

        foreach (var window in _shell.Windows)
        {
            if (window.Frame is { } frame)
            {
                var box = frame.OuterBox;
                BasinReport.Line($"SURFACE frame {box.Width}x{box.Height}@{window.Scale} strips=4");
            }
        }

        BasinReport.Line($"CURSOR {_cursor.Showing} drawn={_cursor.DrawnBy} on={_cursor.CursorOutput?.Name ?? "none"}");
        BasinReport.Line($"POPUPS {_uiDriver.Popups.Count}");
        if (_workspaces is { } workspaces)
        {
            BasinReport.Line($"WORKSPACE {workspaces.Active + 1}/{workspaces.Count} sliding={workspaces.IsSliding} progress={workspaces.SlideProgress:F3}");
        }
    }
}
