using Basin.Host;
using Basin.Cli;
using Basin.Ipc;
using Basin.Diagnostics;

namespace MauiComp;

internal sealed partial class MauiComp
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;

    private void WireIpc()
    {
        _ipc = _options.Ipc.Attach(_host.Loop, _services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "maui-comp",
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = RendererName,
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

        _report.Register(methods, "mauicomp/shot", "shot {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                WriteScreenshot(path);
            }
        });

        _report.Register(methods, "mauicomp/shotraw", "shotraw {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                WritePresented(path);
            }
        });

        _report.Register(methods, "mauicomp/planeshot", "planeshot {prefix}", (ref IpcParams p, IpcReply _) =>
        {
            var prefix = p.GetString("prefix");
            if (!p.Failed)
            {
                WritePlanes(prefix);
            }
        });

        _report.Register(methods, "mauicomp/where", "where", PrintState);

        _report.Register(methods, "mauicomp/launcher", "launcher [{action}]", (ref IpcParams p, IpcReply _) =>
        {
            string? action = p.TryGetString("action", out var named) ? named : null;
            if (!p.Failed)
            {
                DriveLauncher(action);
            }
        });

        _report.Register(methods, "mauicomp/run", "run", OpenRun);
        _report.Register(methods, "mauicomp/run-close", "run close", () => _runDialog?.Close());

        _report.Register(methods, "mauicomp/switcher", "switcher {action}", (ref IpcParams p, IpcReply _) =>
        {
            var action = p.GetString("action");
            if (!p.Failed)
            {
                DriveSwitcher(action);
            }
        });

        _report.Register(methods, "mauicomp/pumps", "pumps {count:int}", (ref IpcParams p, IpcReply reply) =>
        {
            var count = p.GetInt("count");
            if (p.Failed)
            {
                return;
            }

            if (count is < 0 or > 1_000_000)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'count' is between 0 and 1000000");
                return;
            }

            MeasurePumps((int)count);
        });

        _report.Register(methods, "mauicomp/clock", "clock {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (p.Failed)
            {
                return;
            }

            _clockTimer?.Remove();
            _clockTimer = null;
            _shell.SetClock(text);
        });

        _report.Register(methods, "mauicomp/theme", "theme {variant}", (ref IpcParams p, IpcReply _) =>
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

        var chrome = new List<PlaneShotChrome>();
        var panels = 0;
        foreach (var elements in _shell.Elements.Values)
        {
            chrome.Add(new PlaneShotChrome($"panel{panels++}", null, elements.PanelSurface.Node.Node));
        }

        chrome.Add(new PlaneShotChrome("startmenu", null, _startMenu?.Node));
        chrome.Add(new PlaneShotChrome("switcher", null, _switcher?.Node));
        var titles = 0;
        foreach (var window in _windows)
        {
            chrome.Add(new PlaneShotChrome($"titlebar{titles++}", null, window.Titlebar?.Node));
        }

        _ = PlaneShot.Write(view, _renderer, prefix, _scene, chrome);
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
        _report.Line($"PUMPS n={count} bytes={after - before}");
    }

    private void PrintState()
    {
        _report.Line($"POINTER {_seat?.PointerX ?? 0} {_seat?.PointerY ?? 0}");
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            _report.Line($"AREA {view.Output.Name} output={box} work={ShellOutputs.WorkArea(box)}");
        }

        foreach (var window in _windows)
        {
            _report.Line($"WINDOW \"{window.Window.Title}\" {window.Geometry} " + $"focused={ReferenceEquals(window, _focused)} maximized={window.Maximized} minimized={window.Minimized} fullscreen={window.Fullscreen} " + $"titlebar={(window.Titlebar is null ? "none" : window.Titlebar.Visible ? "shown" : "hidden")}");
        }

        _report.Line($"SWITCHER {(_switcher?.IsOpen == true ? "open" : "closed")}");
        _report.Line($"ANIMATING {(_animations.IsRunning ? "yes" : "no")}");
        _report.Line($"STARTMENU {(_startMenu?.IsOpen == true ? "open" : "closed")} " + $"programs={(_startMenu?.IsProgramsOpen == true ? "open" : "closed")}");
        _report.Line($"RUN {(_runDialog?.IsOpen == true ? "open" : "closed")} text=\"{_runDialog?.Text}\"");
        if (_startMenu?.Surface is { } menuSurface)
        {
            var size = menuSurface.Size;
            _report.Line($"SURFACE startmenu {size.Width}x{size.Height}@{size.Scale} at={menuSurface.PositionX},{menuSurface.PositionY}");
        }

        if (_runDialog?.Surface is { } runSurface)
        {
            var size = runSurface.Size;
            _report.Line($"SURFACE run {size.Width}x{size.Height}@{size.Scale} at={_runDialog.X},{_runDialog.Y}");
        }

        foreach (var elements in _shell.Elements.Values)
        {
            if (elements.PanelSurface.Surface is { } panel)
            {
                var size = panel.Size;
                _report.Line($"SURFACE panel {size.Width}x{size.Height}@{size.Scale}");
            }
        }

        foreach (var popup in _uiDriver.Popups)
        {
            if (popup.Surface is { } surface)
            {
                var size = surface.Size;
                _report.Line($"SURFACE popup {size.Width}x{size.Height}@{size.Scale} at={popup.X},{popup.Y}");
            }
        }

        var blurredTitlebars = 0;
        foreach (var window in _windows)
        {
            if (window.Titlebar?.Blurred == true)
            {
                blurredTitlebars++;
            }
        }

        var blurredPanels = 0;
        foreach (var elements in _shell.Elements.Values)
        {
            if (elements.PanelSurface.Node.Node.BackdropEffect is not null)
            {
                blurredPanels++;
            }
        }

        _report.Line(
            $"BLUR {(_blur is null ? "unavailable" : $"strength={_blur.Options.Strength}")} taskbar={blurredPanels} "
            + $"startmenu={(_startMenu?.Blurred == true ? "on" : "off")} switcher={(_switcher?.Blurred == true ? "on" : "off")} "
            + $"titlebars={blurredTitlebars}");
        _report.Line($"POPUPS {_uiDriver.Popups.Count}");
        _report.Line($"SCOPES {_mauiSurfaces.Live}");
        var hit = _scene.SurfaceAt(_seat?.PointerX ?? 0, _seat?.PointerY ?? 0);
        _report.Line($"KEYBOARD client={(Seat.Keyboard.Focus is null ? "none" : "surface")} " + $"ui={(_seat?.Router.KeyboardFocus is null ? "none" : "surface")} " + $"text={(_seat?.Router.WantsTextInput == true ? "yes" : "no")}");
        _report.Line($"HIT scene={(hit?.Surface is null ? "none" : "surface")} " + $"focus={(Seat.Pointer.Focus is null ? "none" : "surface")} " + $"shell={(_seat?.IsOverShell == true ? "yes" : "no")}");
    }
}
