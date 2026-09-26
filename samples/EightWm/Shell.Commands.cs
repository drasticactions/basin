using Avalonia;
using Avalonia.VisualTree;
using Basin.Host;
using System.Globalization;
using Basin;
using Basin.Cli;
using Basin.Ipc;
using Basin.Seat;
using Xkb;
using Wayland;
using Wayland.Server;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;
    private IpcPendingReply? _shotReply;

    private void WireIpc()
    {
        _ipc = _options.Ipc.Attach(_host.Loop, _services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "eight-wm",
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = _options.Renderer,
            Quit = Stop,
        });
        _ipc.SyntheticInput = _seat.Injector;
        RegisterCommands(_ipc.Methods);
        _ipc.Start();
        _ipc.StartLineFront();
    }

    private void UnwireIpc()
    {
        _ipc?.Dispose();
        _ipc = null;
    }

    private ShellView CommandView => Views.Count > 0 ? Views[0] : PrimaryView;

    internal void HandleCommand(string line) => _ipc?.LineFront?.Execute(line);

    private void RegisterCommands(IpcMethodRegistry methods)
    {
        _report.AddLine(methods, IpcMethodNames.SessionQuit, "quit");

        _report.Register(methods, "eightwm/start", "start", () => ToggleStart(CommandView));
        _report.Register(methods, "eightwm/close", "close", CloseFocused);
        _report.Register(methods, "eightwm/switcher", "switcher", PrintSwitcher);
        _report.Register(methods, "eightwm/mru", "mru", PrintMru);
        _report.Register(methods, "eightwm/chrome", "chrome", PrintChrome);
        _report.Register(methods, "eightwm/where", "where", PrintScene);
        _report.Register(methods, "eightwm/cells", "cells", PrintCells);
        _report.Register(methods, "eightwm/tiles", "tiles", PrintTiles);

        _report.Register(methods, "eightwm/snap", "snap {which} {side}", (ref IpcParams p, IpcReply _) =>
        {
            var which = p.GetString("which");
            var side = p.GetString("side");
            if (!p.Failed)
            {
                SnapCommand(which, side);
            }
        });

        _report.Register(methods, "eightwm/split", "split {position:number}", (ref IpcParams p, IpcReply _) =>
        {
            var position = p.GetDouble("position");
            if (!p.Failed)
            {
                SplitCommand(position);
            }
        });

        _report.Register(methods, "eightwm/eject", "eject {index:int}", (ref IpcParams p, IpcReply _) =>
        {
            var index = p.GetInt("index");
            if (!p.Failed)
            {
                EjectCommand((int)index);
            }
        });

        _report.Register(methods, "eightwm/launch", "launch {command...}", (ref IpcParams p, IpcReply _) =>
        {
            var command = p.GetString("command");
            if (!p.Failed)
            {
                Spawn(command);
            }
        });

        _report.Register(methods, "eightwm/tap", "tap {which}", (ref IpcParams p, IpcReply _) =>
        {
            var which = p.GetString("which");
            if (!p.Failed)
            {
                TapCommand(which);
            }
        });

        _report.Register(methods, "eightwm/edge", "edge {side} [{progress:number}]", (ref IpcParams p, IpcReply _) =>
        {
            var side = p.GetString("side");
            var progress = p.TryGetDouble("progress", out var given) ? given : 1.0;
            if (!p.Failed)
            {
                EdgeCommand(side, progress, hold: false);
            }
        });

        _report.Register(methods, "eightwm/edge-hold", "edge {side} hold {fraction:number}", (ref IpcParams p, IpcReply _) =>
        {
            var side = p.GetString("side");
            var fraction = p.GetDouble("fraction");
            if (!p.Failed)
            {
                EdgeCommand(side, fraction, hold: true);
            }
        });

        RegisterToggle(methods, "eightwm/switcher-dock", "switcher", state => DockSwitcher(CommandView, state));
        RegisterToggle(methods, "eightwm/title", "title", state => ShowTitle(CommandView, state));
        RegisterToggle(methods, "eightwm/charms", "charms", state => ShowCharms(CommandView, state));

        _report.Register(methods, "eightwm/titledrag", "titledrag {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            if (!p.Failed)
            {
                TitleDragCommand(x, y);
            }
        });

        _report.Register(methods, "eightwm/titlegrab", "titlegrab", TitleGrabCommand);

        _report.Register(methods, "eightwm/titlemove", "titlemove {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            if (!p.Failed)
            {
                TitleStepCommand(x, y, drop: false);
            }
        });

        _report.Register(methods, "eightwm/titledrop", "titledrop {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            if (!p.Failed)
            {
                TitleStepCommand(x, y, drop: true);
            }
        });

        _report.Register(methods, "eightwm/charm", "charm {which}", (ref IpcParams p, IpcReply _) =>
        {
            var which = p.GetString("which");
            if (!p.Failed)
            {
                CharmCommand(which);
            }
        });

        _report.Register(methods, "eightwm/zoom", "zoom {which}", (ref IpcParams p, IpcReply _) =>
        {
            var which = p.GetString("which");
            if (!p.Failed)
            {
                ToggleZoom(CommandView, which == "out");
            }
        });

        _report.Register(methods, "eightwm/apps-sort", "apps sort {sort}", (ref IpcParams p, IpcReply _) =>
        {
            var sort = p.GetString("sort");
            if (!p.Failed)
            {
                AppsSortCommand(sort);
            }
        });

        RegisterToggle(methods, "eightwm/apps", "apps", state => ShowApps(CommandView, state));

        _report.Register(methods, "eightwm/move", "move {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            if (!p.Failed)
            {
                _seat.WarpTo(x, y);
            }
        });

        _report.Register(methods, "eightwm/click", "click", () => _seat.ClickAt());
        _report.Register(methods, "eightwm/cursor", "cursor", () => _report.Line($"CURSOR {_seat.CursorState}"));

        _report.Register(methods, "eightwm/touch", "touch {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            if (!p.Failed)
            {
                _seat.TapAt(CommandView.Box.Width * x, CommandView.Box.Height * y);
            }
        });

        _report.Register(methods, "eightwm/touchdrag", "touchdrag {x:number} {y:number} {dx:number} {dy:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            var dx = p.GetDouble("dx");
            var dy = p.GetDouble("dy");
            if (!p.Failed)
            {
                _seat.DragTouch(
                    CommandView.Box.Width * x, CommandView.Box.Height * y,
                    CommandView.Box.Width * dx, CommandView.Box.Height * dy, 12);
            }
        });

        _report.Register(methods, "eightwm/mousedown", "mousedown", () => _seat.ButtonAt(pressed: true));
        _report.Register(methods, "eightwm/mouseup", "mouseup", () => _seat.ButtonAt(pressed: false));

        _report.Register(methods, "eightwm/key", "key {chord}", (ref IpcParams p, IpcReply _) =>
        {
            var chord = p.GetString("chord");
            if (!p.Failed)
            {
                KeyCommand(chord);
            }
        });

        _report.Register(methods, "eightwm/press", "press {which} [{x:number}] [{y:number}]", (ref IpcParams p, IpcReply _) =>
        {
            var which = p.GetString("which");
            var x = p.TryGetDouble("x", out var px) ? px : 0.5;
            var y = p.TryGetDouble("y", out var py) ? py : 0.5;
            if (!p.Failed)
            {
                PressCommand(which, x, y);
            }
        });

        _report.Register(methods, "eightwm/release", "release", ReleaseCommand);

        _report.Register(methods, "eightwm/select", "select {which}", (ref IpcParams p, IpcReply _) =>
        {
            var which = p.GetString("which");
            if (!p.Failed)
            {
                SelectCommand(which);
            }
        });

        _report.Register(methods, "eightwm/shotnow", "shotnow {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                _shotPath = path;
                _shotView = 0;
            }
        });

        _report.Register(methods, "eightwm/planeshot", "planeshot {prefix}", (ref IpcParams p, IpcReply _) =>
        {
            var prefix = p.GetString("prefix");
            if (p.Failed)
            {
                return;
            }

            PlaneShot.Write(CommandView.Driver, _renderer, prefix, _scene,
                CommandView.Charms is { } charms
                    ? [new PlaneShotChrome("charms", null, charms.BarNode), new PlaneShotChrome("pane", null, charms.PaneNode)]
                    : null);
        });

        _report.Register(methods, "eightwm/shot", "shot {path} [{index:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var path = p.GetString("path");
            var index = p.TryGetInt("index", out var at) ? (int)at : 0;
            if (p.Failed)
            {
                return;
            }

            if (index < 0 || index >= Views.Count)
            {
                reply.Error(IpcErrorCodes.NotFound, $"no output {index}");
                return;
            }

            if (_shotReply is { } superseded)
            {
                _shotReply = null;
                _ = IpcLineReport.Complete(superseded);
            }

            _shotPath = path;
            _shotView = index;
            _shotReply = reply.Defer();
            _outputs.RepaintNow(Views[index].Driver);
        });

        _report.Register(methods, "eightwm/reload", "reload", Reload);

        _report.Register(methods, "eightwm/settings", "settings", () =>
            _report.Line($"SETTINGS hot_corners={(HotCornersOn ? "on" : "off")} " + $"animations={(AnimationsOn ? "on" : "off")} edge_band={EdgeBandNow} " + $"min_width={MinWidthNow} max_cells={Configuration.MaxCells} " + $"start_output={StartOutputNow} rules={Configuration.Rules.Count} " + $"theme={(DarkNow ? "dark" : "light")} accent=#{AccentNow & 0xffffff:x6}"));
    }

    private void RegisterToggle(IpcMethodRegistry methods, string name, string word, Action<bool> apply) =>
        _report.Register(methods, name, word + " {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                apply(state == "on");
            }
        });

    private static int Number(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static double Fraction(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private void SnapCommand(string which, string side)
    {
        var view = CommandView;
        var app = ResolveApp(which, view);
        if (app is null)
        {
            _report.Line($"ERR no app '{which}'");
            return;
        }

        var at = side switch
        {
            "left" or "top" => 0,
            "right" or "bottom" => view.Host.SlotCount,
            _ => Number(side),
        };

        _report.Line(Snap(app, view, at) ? $"SNAP {app.AppId} {at}" : $"ERR no room for '{which}'");
    }

    private void SplitCommand(double fraction)
    {
        var view = CommandView;
        var app = view.Host.Previous();
        if (app is null)
        {
            _report.Line($"ERR nothing to split with");
            return;
        }

        var at = view.Host.SlotCount;
        _report.Line(Snap(app, view, at, fraction <= 0 || fraction >= 1 ? 0.5 : fraction)
                ? $"SNAP {app.AppId} {at}"
                : "ERR no room to split");
    }

    private void EjectCommand(int index)
    {
        var view = CommandView;
        if (index < 0 || index >= view.Host.Cells.Count)
        {
            _report.Line($"ERR no cell {index}");
            return;
        }

        var app = view.Host.Cells[index];
        view.Host.Eject(app);
        Relayout(view);
        _report.Line($"EJECT {app.AppId}");
    }

    private AppWindow? ResolveApp(string which, ShellView view)
    {
        if (int.TryParse(which, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return index >= 0 && index < view.Host.Mru.Count ? view.Host.Mru[index] : null;
        }

        foreach (var app in _apps)
        {
            if (app.AppId == which)
            {
                return app;
            }
        }

        return null;
    }

    private void PrintChrome()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            _report.Line($"CHROME output={i} box={view.Box.Width}x{view.Box.Height} scale={view.Scale} " + $"dim={State(view.Dim.Enabled)} splash={State(view.Splash is { Enabled: true })}");
            if (view.Charms is { } charms)
            {
                _report.Line($"  charms {State(charms.Visible)} retired={charms.IsRetired} " + $"clock={charms.ClockShown} paneshown={charms.PaneShown} hot={charms.Hot} " + $"pane={State(charms.OpenPane != Charm.None)} " + $"bar={Fmt(charms.BarBox)} panebox={Fmt(charms.PaneBox)}");
            }

            if (view.Title is { } title)
            {
                _report.Line($"  title {State(title.Visible)} box={Fmt(title.Box)} close={Fmt(title.CloseBox)}");
            }

            if (view.Switcher is { } rail)
            {
                _report.Line($"  rail {State(view.SwitcherDocked)} box={Fmt(rail.Box)}");
            }

            if (view.StartView is { } start)
            {
                var (width, height, rows) = StartExtent(start);
                _report.Line($"  start grid={width:F0}x{height:F0} rows={rows}");
            }
        }
    }

    private static string State(bool open) => open ? "open" : "closed";

    private static string Fmt(in Basin.Box box) => $"{box.X},{box.Y},{box.Width}x{box.Height}";

    private void PrintScene()
    {
        var boxes = new List<Basin.SurfaceBox>();
        _scene.CollectSurfaces(boxes);
        _report.Line($"SCENE surfaces={boxes.Count}");
        foreach (var entry in boxes)
        {
            _report.Line($"  surface {entry.Box.X},{entry.Box.Y} {entry.Box.Width}x{entry.Box.Height} " + $"buffer={(entry.Surface.Current.Buffer is null ? "none" : "yes")}");
        }

        foreach (var app in _apps)
        {
            _report.Line($"  app {app.AppId} cell={app.Cell.X},{app.Cell.Y},{app.Cell.Width}x{app.Cell.Height} " + $"slot={app.Slot.X},{app.Slot.Y} enabled={app.Slot.Enabled} parked={app.IsParked}");
        }
    }

    private void PrintCells()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            var widths = string.Join(',', view.Host.Widths);
            var boxes = string.Join(' ', view.Host.Cells.Select(app =>
                $"{app.AppId}:{app.Cell.X},{app.Cell.Y},{app.Cell.Width}x{app.Cell.Height}"));
            var vacant = view.Host.HasVacancy
                ? $" vacant={view.Host.VacantSlot}:{view.Host.VacantArea.X},{view.Host.VacantArea.Y}," +
                  $"{view.Host.VacantArea.Width}x{view.Host.VacantArea.Height}"
                : string.Empty;
            _report.Line($"CELLS output={i} portrait={(view.IsPortrait ? "yes" : "no")} widths=[{widths}] {boxes}{vacant}");
        }
    }

    private static (double Width, double Height, int Rows) StartExtent(StartView start)
    {
        var scroller = start.TileList.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().FirstOrDefault();
        var extent = scroller?.Extent ?? default;
        return (extent.Width, extent.Height, (int)(extent.Height / Tile.Cell));
    }

    private static double ScrollOf(Avalonia.Controls.Control list) =>
        list.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().FirstOrDefault()?.Offset.X ?? 0;

    private static Basin.Box? TileBox(ShellView view, int index)
    {
        if (view.StartView is not { } start || start.TileList.ContainerFromIndex(index) is not { } container ||
            container.TranslatePoint(default, start) is not { } origin)
        {
            return null;
        }

        return new Basin.Box(
            (int)Math.Round(origin.X + view.StartFrame.X),
            (int)Math.Round(origin.Y + view.StartFrame.Y),
            (int)Math.Round(container.Bounds.Width),
            (int)Math.Round(container.Bounds.Height));
    }

    private Tile? TileOf(ShellView view, string which)
    {
        var index = Number(which);
        var tiles = view.StartModel.Tiles;
        if (index >= 0 && index < tiles.Count)
        {
            return tiles[index];
        }

        _report.Line($"ERR no tile {which}");
        return null;
    }

    private void PrintTiles()
    {
        var view = CommandView;
        if (view.StartView is not { } start)
        {
            _report.Line($"ERR no start screen");
            return;
        }

        var (width, _, rows) = StartExtent(start);
        var apps = view.AppsView is { } appsView ? ScrollOf(appsView.List) : 0;
        _report.Line($"TILES groups={view.StartModel.Groups.Count} rows={rows} width={width:F0} " + $"pan={-ScrollOf(start.TileList):F0} axis=Horizontal apps={-apps:F0}");
        var tiles = view.StartModel.Tiles;
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var box = TileBox(view, index) is { } placed
                ? $"{placed.X},{placed.Y},{placed.Width}x{placed.Height}"
                : "-";
            _report.Line($"TILE {index} group={tile.Group} name={tile.Name} box={box}");
        }
    }

    private void AppsSortCommand(string which)
    {
        var sort = which switch
        {
            "name" => AppsSort.Name,
            "date" => AppsSort.DateInstalled,
            "used" => AppsSort.MostUsed,
            "category" => AppsSort.Category,
            _ => (AppsSort?)null,
        };
        if (sort is not { } chosen)
        {
            _report.Line($"ERR no sort '{which}'");
            return;
        }

        ShowApps(CommandView, true);
        CommandView.StartModel.AppsSort = chosen;
    }

    private void TapCommand(string which)
    {
        var view = CommandView;
        if (TileOf(view, which) is { } tile)
        {
            LaunchTile(view, tile);
        }
    }

    private void TitleGrabCommand()
    {
        var view = CommandView;
        ShowTitle(view, true);
        if (view.Title is not { Visible: true } title)
        {
            _report.Line($"ERR no titlebar");
            return;
        }

        var box = title.Box;
        _report.Line(TitlePress(view, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0), ShellSeat.PointerTouchId)
                ? "TITLE grab"
                : "ERR the titlebar refused the press");
    }

    private void TitleStepCommand(double fractionX, double fractionY, bool drop)
    {
        var view = CommandView;
        var x = view.Box.Width * fractionX;
        var y = view.Box.Height * fractionY;
        var handled = drop
            ? TitleRelease(view, x, y, ShellSeat.PointerTouchId)
            : TitleMove(view, x, y, ShellSeat.PointerTouchId);
        if (!handled)
        {
            _report.Line($"ERR no titlebar drag in flight");
        }
    }

    private void TitleDragCommand(double fractionX, double fractionY)
    {
        var view = CommandView;
        ShowTitle(view, true);
        if (view.Title is not { Visible: true } title)
        {
            _report.Line($"ERR no titlebar");
            return;
        }

        var box = title.Box;
        var startX = box.X + (box.Width / 2.0);
        var startY = box.Y + (box.Height / 2.0);
        if (!TitlePress(view, startX, startY, ShellSeat.PointerTouchId))
        {
            _report.Line($"ERR the titlebar refused the press");
            return;
        }

        var endX = view.Box.Width * fractionX;
        var endY = view.Box.Height * fractionY;
        for (var step = 1; step <= 6; step++)
        {
            TitleMove(
                view, startX + ((endX - startX) * step / 6.0), startY + ((endY - startY) * step / 6.0),
                ShellSeat.PointerTouchId);
        }

        TitleRelease(view, endX, endY, ShellSeat.PointerTouchId);
    }

    private void EdgeCommand(string side, double progress, bool hold)
    {
        var view = CommandView;
        var edge = side switch
        {
            "left" => Basin.Seat.ScreenEdge.Left,
            "right" => Basin.Seat.ScreenEdge.Right,
            "top" => Basin.Seat.ScreenEdge.Top,
            "bottom" => Basin.Seat.ScreenEdge.Bottom,
            _ => Basin.Seat.ScreenEdge.None,
        };
        if (edge == Basin.Seat.ScreenEdge.None)
        {
            _report.Line($"ERR no edge '{side}'");
            return;
        }

        RunSyntheticEdge(view, edge, progress <= 0 ? 1.0 : progress, hold);
    }

    private void PrintSwitcher()
    {
        var view = CommandView;
        _report.Line($"SWITCHER docked={(view.SwitcherDocked ? "yes" : "no")} entries={view.Switcher?.Count ?? 0}");
    }

    private void CharmCommand(string which)
    {
        var view = CommandView;
        if (!Enum.TryParse<Charm>(which, ignoreCase: true, out var charm) || charm == Charm.None)
        {
            _report.Line($"ERR no charm '{which}'");
            return;
        }

        ShowCharms(view, true);
        if (!ActivateCharm(view, charm))
        {
            _report.Line($"ERR charm '{which}' did nothing");
        }
    }

    private void KeyCommand(string chord)
    {
        var parts = chord.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var modifiers = new List<string>();
        var name = string.Empty;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "super":
                    modifiers.Add("Super_L");
                    break;

                case "shift":
                    modifiers.Add("Shift_L");
                    break;

                case "alt":
                    modifiers.Add("Alt_L");
                    break;

                case "ctrl":
                case "control":
                    modifiers.Add("Control_L");
                    break;

                default:
                    name = part;
                    break;
            }
        }

        var codes = new List<uint>();
        foreach (var modifier in modifiers)
        {
            if (KeycodeOf(modifier) is not { } code)
            {
                _report.Line($"ERR no key '{modifier}'");
                return;
            }

            codes.Add(code);
        }

        if (name.Length > 0)
        {
            if (KeycodeOf(name) is not { } code)
            {
                _report.Line($"ERR no key '{name}'");
                return;
            }

            codes.Add(code);
        }

        for (var i = 0; i < codes.Count; i++)
        {
            _seat.InjectKey(codes[i], pressed: true);
        }

        for (var i = codes.Count - 1; i >= 0; i--)
        {
            _seat.InjectKey(codes[i], pressed: false);
        }

        _report.Line($"KEY {chord}");
    }

    private uint? KeycodeOf(string name)
    {
        var wanted = XkbKeysym.FromName(name);
        if (wanted.Value == 0)
        {
            return null;
        }

        for (uint code = 0; code < 256; code++)
        {
            if (Seat.Keyboard.KeysymFor(code) == wanted)
            {
                return code;
            }
        }

        return null;
    }

    internal const int PressTouchId = 90;

    private void PressCommand(string which, double fractionX, double fractionY)
    {
        var view = CommandView;
        var index = Number(which);
        if (TileOf(view, which) is not { } tile)
        {
            return;
        }

        if (TileBox(view, index) is not { } box)
        {
            _report.Line($"ERR tile {which} is not realized");
            return;
        }

        var x = view.Box.X + box.X + (box.Width * fractionX);
        var y = view.Box.Y + box.Y + (box.Height * fractionY);
        _router.TouchDown((uint)Environment.TickCount, PressTouchId, x, y);
        _report.Line($"PRESS {tile.Name} at {fractionX},{fractionY}");
    }

    private void ReleaseCommand()
    {
        _router.TouchCancel();
        _report.Line($"RELEASE");
    }

    private void SelectCommand(string which)
    {
        var view = CommandView;
        if (view.StartView is not { } start || TileOf(view, which) is null)
        {
            return;
        }

        var index = Number(which);
        if (start.TileList.Selection.IsSelected(index))
        {
            start.TileList.Selection.Deselect(index);
        }
        else
        {
            start.TileList.Selection.Select(index);
        }
    }

    private void PrintMru()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            var cells = string.Join(',', view.Host.Cells.Select(app => app.AppId));
            var mru = string.Join(',', view.Host.Mru.Select(app => app.AppId));
            _report.Line($"MRU output={i} start={(view.StartVisible ? "on" : "off")} cells=[{cells}] mru=[{mru}]");
        }
    }
}
