using Basin.Cli;
using Basin.Diagnostics;

namespace MauiComp;

internal sealed partial class MauiComp
{
    private void WireStdin()
    {
        _stdinCommands = new StdinCommands(_host.Loop, HandleCommand);
        _stdinCommands.CommandFailed += (command, error) =>
            BasinReport.Line(CompositorLines.CommandFailed(command, error));
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
            case ["launcher"]:
                DriveLauncher(null);
                break;
            case ["launcher", var action]:
                DriveLauncher(action);
                break;
            case ["run"]:
                OpenRun();
                break;
            case ["run", "close"]:
                _runDialog?.Close();
                break;
            case ["switcher", var action]:
                DriveSwitcher(action);
                break;
            case ["pumps", var count]:
                MeasurePumps(int.Parse(count, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case ["clock", .. var words] when words.Length > 0:
                _clockTimer?.Remove();
                _clockTimer = null;
                _shell.SetClock(string.Join(' ', words));
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

    private void MeasurePumps(int count)
    {
        for (var i = 0; i < 8; i++)
        {
            _uiDriver.Pump();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
        {
            _ui.Pump();
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        BasinReport.Line($"PUMPS n={count} bytes={after - before}");
    }

    private void PrintState()
    {
        BasinReport.Line($"POINTER {_seat?.PointerX ?? 0} {_seat?.PointerY ?? 0}");
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            BasinReport.Line($"AREA {view.Output.Name} output={box} work={ShellOutputs.WorkArea(box)}");
        }

        foreach (var window in _windows)
        {
            BasinReport.Line($"WINDOW \"{window.Window.Title}\" {window.Geometry} " + $"focused={ReferenceEquals(window, _focused)} maximized={window.Maximized} minimized={window.Minimized} fullscreen={window.Fullscreen} " + $"titlebar={(window.Titlebar is null ? "none" : window.Titlebar.Visible ? "shown" : "hidden")}");
        }

        BasinReport.Line($"SWITCHER {(_switcher?.IsOpen == true ? "open" : "closed")}");
        BasinReport.Line($"ANIMATING {(_animations.IsRunning ? "yes" : "no")}");
        BasinReport.Line($"STARTMENU {(_startMenu?.IsOpen == true ? "open" : "closed")} " + $"programs={(_startMenu?.IsProgramsOpen == true ? "open" : "closed")}");
        BasinReport.Line($"RUN {(_runDialog?.IsOpen == true ? "open" : "closed")} text=\"{_runDialog?.Text}\"");
        if (_startMenu?.Surface is { } menuSurface)
        {
            var size = menuSurface.Size;
            BasinReport.Line($"SURFACE startmenu {size.Width}x{size.Height}@{size.Scale} at={menuSurface.PositionX},{menuSurface.PositionY}");
        }

        if (_runDialog?.Surface is { } runSurface)
        {
            var size = runSurface.Size;
            BasinReport.Line($"SURFACE run {size.Width}x{size.Height}@{size.Scale} at={_runDialog.X},{_runDialog.Y}");
        }

        foreach (var elements in _shell.Elements.Values)
        {
            if (elements.PanelSurface.Surface is { } panel)
            {
                var size = panel.Size;
                BasinReport.Line($"SURFACE panel {size.Width}x{size.Height}@{size.Scale}");
            }
        }

        foreach (var popup in _uiDriver.Popups)
        {
            if (popup.Surface is { } surface)
            {
                var size = surface.Size;
                BasinReport.Line($"SURFACE popup {size.Width}x{size.Height}@{size.Scale} at={popup.X},{popup.Y}");
            }
        }

        BasinReport.Line($"POPUPS {_uiDriver.Popups.Count}");
        BasinReport.Line($"SCOPES {_mauiSurfaces.Live}");
        var hit = _scene.SurfaceAt(_seat?.PointerX ?? 0, _seat?.PointerY ?? 0);
        BasinReport.Line($"KEYBOARD client={(Seat.Keyboard.Focus is null ? "none" : "surface")} " + $"ui={(_seat?.Router.KeyboardFocus is null ? "none" : "surface")} " + $"text={(_seat?.Router.WantsTextInput == true ? "yes" : "no")}");
        BasinReport.Line($"HIT scene={(hit?.Surface is null ? "none" : "surface")} " + $"focus={(Seat.Pointer.Focus is null ? "none" : "surface")} " + $"shell={(_seat?.IsOverShell == true ? "yes" : "no")}");
    }
}
