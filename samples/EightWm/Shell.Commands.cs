using System.Globalization;
using Basin;
using Basin.Cli;
using Basin.Seat;
using Xkb;
using Wayland;
using Wayland.Server;

namespace EightWm;

internal sealed partial class Shell
{
    private StdinCommands? _stdin;

    private void WireStdin() => _stdin = new StdinCommands(_host.Loop, HandleCommand);

    private void UnwireStdin()
    {
        _stdin?.Stop();
        _stdin = null;
    }

    private ShellView CommandView => Views.Count > 0 ? Views[0] : PrimaryView;

    internal void HandleCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case []:
                return;

            case ["start"]:
                ToggleStart(CommandView);
                break;

            case ["close"]:
                CloseFocused();
                break;

            case ["switcher"]:
                PrintSwitcher();
                break;

            case ["mru"]:
                PrintMru();
                break;

            case ["chrome"]:
                PrintChrome();
                break;

            case ["where"]:
                PrintScene();
                break;

            case ["cells"]:
                PrintCells();
                break;

            case ["snap", var which, var side]:
                SnapCommand(which, side);
                break;

            case ["split", var position]:
                SplitCommand(Fraction(position));
                break;

            case ["eject", var which]:
                EjectCommand(Number(which));
                break;

            case ["launch", ..]:
                Spawn(string.Join(' ', parts[1..]));
                break;

            case ["tiles"]:
                PrintTiles();
                break;

            case ["tap", var which]:
                TapCommand(which);
                break;

            case ["edge", var side]:
                EdgeCommand(side, 1.0, hold: false);
                break;

            case ["edge", var side, var progress]:
                EdgeCommand(side, Fraction(progress), hold: false);
                break;

            case ["edge", var side, "hold", var fraction]:
                EdgeCommand(side, Fraction(fraction), hold: true);
                break;

            case ["switcher", var state]:
                DockSwitcher(CommandView, state == "on");
                break;

            case ["title", var state]:
                ShowTitle(CommandView, state == "on");
                break;

            case ["titledrag", var dx, var dy]:
                TitleDragCommand(Fraction(dx), Fraction(dy));
                break;

            case ["titlegrab"]:
                TitleGrabCommand();
                break;

            case ["titlemove", var mx, var my]:
                TitleStepCommand(Fraction(mx), Fraction(my), drop: false);
                break;

            case ["titledrop", var dx, var dy]:
                TitleStepCommand(Fraction(dx), Fraction(dy), drop: true);
                break;

            case ["charms", var state]:
                ShowCharms(CommandView, state == "on");
                break;

            case ["charm", var which]:
                CharmCommand(which);
                break;

            case ["zoom", var which]:
                ToggleZoom(CommandView, which == "out");
                break;

            case ["apps", var state]:
                ShowApps(CommandView, state == "on");
                break;

            case ["move", var mx, var my]:
                _seat.WarpTo(Fraction(mx), Fraction(my));
                break;

            case ["click"]:
                _seat.ClickAt();
                break;

            case ["cursor"]:
                Console.WriteLine($"CURSOR {_seat.CursorState}");
                break;

            case ["touch", var tx, var ty]:
                _seat.TapAt(CommandView.Box.Width * Fraction(tx), CommandView.Box.Height * Fraction(ty));
                break;

            case ["mousedown"]:
                _seat.ButtonAt(pressed: true);
                break;

            case ["mouseup"]:
                _seat.ButtonAt(pressed: false);
                break;

            case ["key", var chord]:
                KeyCommand(chord);
                break;

            case ["press", var which]:
                PressCommand(which, 0.5, 0.5);
                break;

            case ["press", var which, var px, var py]:
                PressCommand(which, Fraction(px), Fraction(py));
                break;

            case ["release"]:
                ReleaseCommand();
                break;

            case ["select", var which]:
                SelectCommand(which);
                break;

            case ["shotnow", var path]:
                _shotPath = path;
                _shotView = 0;
                break;

            case ["shot", var path]:
                _shotPath = path;
                _shotView = 0;
                _outputs.RepaintNow(CommandView.Driver);
                break;

            case ["shot", var path, var index]:
                _shotPath = path;
                _shotView = Number(index);
                _outputs.RepaintNow(Views[_shotView].Driver);
                break;

            case ["reload"]:
                Reload();
                break;

            case ["settings"]:
                Console.WriteLine(
                    $"SETTINGS hot_corners={(HotCornersOn ? "on" : "off")} " +
                    $"animations={(AnimationsOn ? "on" : "off")} edge_band={EdgeBandNow} " +
                    $"min_width={MinWidthNow} max_cells={Configuration.MaxCells} " +
                    $"start_output={StartOutputNow} rules={Configuration.Rules.Count}");
                break;

            case ["quit"]:
                Stop();
                break;

            default:
                Console.WriteLine($"ERR unknown command '{line}'");
                break;
        }
    }

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
            Console.WriteLine($"ERR no app '{which}'");
            return;
        }

        var at = side switch
        {
            "left" or "top" => 0,
            "right" or "bottom" => view.Host.SlotCount,
            _ => Number(side),
        };

        Console.WriteLine(Snap(app, view, at) ? $"SNAP {app.AppId} {at}" : $"ERR no room for '{which}'");
    }

    private void SplitCommand(double fraction)
    {
        var view = CommandView;
        var app = view.Host.Previous();
        if (app is null)
        {
            Console.WriteLine("ERR nothing to split with");
            return;
        }

        var at = view.Host.SlotCount;
        Console.WriteLine(
            Snap(app, view, at, fraction <= 0 || fraction >= 1 ? 0.5 : fraction)
                ? $"SNAP {app.AppId} {at}"
                : "ERR no room to split");
    }

    private void EjectCommand(int index)
    {
        var view = CommandView;
        if (index < 0 || index >= view.Host.Cells.Count)
        {
            Console.WriteLine($"ERR no cell {index}");
            return;
        }

        var app = view.Host.Cells[index];
        view.Host.Eject(app);
        Relayout(view);
        Console.WriteLine($"EJECT {app.AppId}");
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
            Console.WriteLine(
                $"CHROME output={i} box={view.Box.Width}x{view.Box.Height} scale={view.Scale} " +
                $"dim={State(view.Dim.Enabled)} splash={State(view.Splash is { Enabled: true })}");
            if (view.Charms is { } charms)
            {
                Console.WriteLine(
                    $"  charms {State(charms.Visible)} retired={charms.IsRetired} " +
                    $"clock={charms.ClockShown} paneshown={charms.PaneShown} hot={charms.Hot} " +
                    $"pane={State(charms.OpenPane != Charm.None)} " +
                    $"bar={Fmt(charms.BarBox)} panebox={Fmt(charms.PaneBox)}");
            }

            if (view.Title is { } title)
            {
                Console.WriteLine(
                    $"  title {State(title.Visible)} box={Fmt(title.Box)} close={Fmt(title.CloseBox)}");
            }

            if (view.Switcher is { } rail)
            {
                Console.WriteLine($"  rail {State(view.SwitcherDocked)} box={Fmt(rail.Box)}");
            }

            if (view.Start is { } start)
            {
                start.Layout();
                Console.WriteLine($"  start grid={start.Grid.Width}x{start.Grid.Height} rows={start.Grid.Rows}");
            }
        }
    }

    private static string State(bool open) => open ? "open" : "closed";

    private static string Fmt(in Basin.Box box) => $"{box.X},{box.Y},{box.Width}x{box.Height}";

    private void PrintScene()
    {
        var boxes = new List<Basin.SurfaceBox>();
        _scene.CollectSurfaces(boxes);
        Console.WriteLine($"SCENE surfaces={boxes.Count}");
        foreach (var entry in boxes)
        {
            Console.WriteLine(
                $"  surface {entry.Box.X},{entry.Box.Y} {entry.Box.Width}x{entry.Box.Height} " +
                $"buffer={(entry.Surface.Current.Buffer is null ? "none" : "yes")}");
        }

        foreach (var app in _apps)
        {
            Console.WriteLine(
                $"  app {app.AppId} cell={app.Cell.X},{app.Cell.Y},{app.Cell.Width}x{app.Cell.Height} " +
                $"slot={app.Slot.X},{app.Slot.Y} enabled={app.Slot.Enabled} parked={app.IsParked}");
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
            Console.WriteLine(
                $"CELLS output={i} portrait={(view.IsPortrait ? "yes" : "no")} widths=[{widths}] {boxes}{vacant}");
        }
    }

    private void PrintTiles()
    {
        var view = CommandView;
        if (view.Start is not { } start)
        {
            Console.WriteLine("ERR no start screen");
            return;
        }

        start.Layout();
        Console.WriteLine(
            $"TILES groups={start.Grid.Groups.Count} rows={start.Grid.Rows} width={start.Grid.Width} " +
            $"pan={start.Pan.Offset:F0} axis={start.Pan.Axis} apps={start.AppsPan.Offset:F0}");
        var index = 0;
        foreach (var group in start.Grid.Groups)
        {
            foreach (var tile in group.Tiles)
            {
                Console.WriteLine(
                    $"TILE {index} group={group.Name} name={tile.Name} " +
                    $"box={tile.Box.X},{tile.Box.Y},{tile.Box.Width}x{tile.Box.Height}");
                index++;
            }
        }
    }

    private void TapCommand(string which)
    {
        var view = CommandView;
        if (view.Start is not { } start)
        {
            Console.WriteLine("ERR no start screen");
            return;
        }

        start.Layout();
        var wanted = Number(which);
        var index = 0;
        foreach (var group in start.Grid.Groups)
        {
            foreach (var tile in group.Tiles)
            {
                if (index++ != wanted)
                {
                    continue;
                }

                LaunchTile(view, tile);
                return;
            }
        }

        Console.WriteLine($"ERR no tile {which}");
    }

    private void TitleGrabCommand()
    {
        var view = CommandView;
        ShowTitle(view, true);
        if (view.Title is not { Visible: true } title)
        {
            Console.WriteLine("ERR no titlebar");
            return;
        }

        var box = title.Box;
        Console.WriteLine(
            TitlePress(view, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0), ShellSeat.PointerTouchId)
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
            Console.WriteLine("ERR no titlebar drag in flight");
        }
    }

    private void TitleDragCommand(double fractionX, double fractionY)
    {
        var view = CommandView;
        ShowTitle(view, true);
        if (view.Title is not { Visible: true } title)
        {
            Console.WriteLine("ERR no titlebar");
            return;
        }

        var box = title.Box;
        var startX = box.X + (box.Width / 2.0);
        var startY = box.Y + (box.Height / 2.0);
        if (!TitlePress(view, startX, startY, ShellSeat.PointerTouchId))
        {
            Console.WriteLine("ERR the titlebar refused the press");
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
            Console.WriteLine($"ERR no edge '{side}'");
            return;
        }

        RunSyntheticEdge(view, edge, progress <= 0 ? 1.0 : progress, hold);
    }

    private void PrintSwitcher()
    {
        var view = CommandView;
        Console.WriteLine(
            $"SWITCHER docked={(view.SwitcherDocked ? "yes" : "no")} entries={view.Switcher?.Count ?? 0}");
    }

    private void CharmCommand(string which)
    {
        var view = CommandView;
        if (!Enum.TryParse<Charm>(which, ignoreCase: true, out var charm) || charm == Charm.None)
        {
            Console.WriteLine($"ERR no charm '{which}'");
            return;
        }

        ShowCharms(view, true);
        if (!ActivateCharm(view, charm))
        {
            Console.WriteLine($"ERR charm '{which}' did nothing");
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
                Console.WriteLine($"ERR no key '{modifier}'");
                return;
            }

            codes.Add(code);
        }

        if (name.Length > 0)
        {
            if (KeycodeOf(name) is not { } code)
            {
                Console.WriteLine($"ERR no key '{name}'");
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

        Console.WriteLine($"KEY {chord}");
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

    private void PressCommand(string which, double fractionX, double fractionY)
    {
        var view = CommandView;
        if (view.Start is not { } start)
        {
            Console.WriteLine("ERR no start screen");
            return;
        }

        start.Layout();
        var wanted = Number(which);
        var index = 0;
        foreach (var group in start.Grid.Groups)
        {
            foreach (var tile in group.Tiles)
            {
                if (index++ != wanted)
                {
                    continue;
                }

                var x = tile.Box.X + (tile.Box.Width * fractionX) + StartScreen.SidePadding + start.Pan.Offset;
                var y = tile.Box.Y + (tile.Box.Height * fractionY) + StartScreen.TopPadding;
                start.Pressed = tile;
                start.SetContact(x, y);
                Console.WriteLine($"PRESS {tile.Name} at {fractionX},{fractionY}");
                return;
            }
        }

        Console.WriteLine($"ERR no tile {which}");
    }

    private void ReleaseCommand()
    {
        if (CommandView.Start is { } start)
        {
            start.Pressed = null;
            Console.WriteLine("RELEASE");
        }
    }

    private void SelectCommand(string which)
    {
        var view = CommandView;
        if (view.Start is not { } start)
        {
            Console.WriteLine("ERR no start screen");
            return;
        }

        start.Layout();
        var wanted = Number(which);
        var index = 0;
        foreach (var group in start.Grid.Groups)
        {
            foreach (var tile in group.Tiles)
            {
                if (index++ != wanted)
                {
                    continue;
                }

                tile.Selected = !tile.Selected;
                start.Invalidate();
                Console.WriteLine($"SELECT {tile.Name} {(tile.Selected ? "on" : "off")}");
                return;
            }
        }

        Console.WriteLine($"ERR no tile {which}");
    }

    private void PrintMru()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            var cells = string.Join(',', view.Host.Cells.Select(app => app.AppId));
            var mru = string.Join(',', view.Host.Mru.Select(app => app.AppId));
            Console.WriteLine(
                $"MRU output={i} start={(view.StartVisible ? "on" : "off")} cells=[{cells}] mru=[{mru}]");
        }
    }
}
