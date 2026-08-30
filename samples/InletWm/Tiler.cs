using Basin.Config;
using Basin.WindowManager;

using Basin.Diagnostics;

namespace InletWm;

internal sealed class Tiler
{
    private const int Gap = 6;
    private const int BorderWidth = 2;

    private static readonly WmColor FocusedBorder = WmColor.FromRgba(0x7a, 0xa2, 0xf7);
    private static readonly WmColor UnfocusedBorder = WmColor.FromRgba(0x3b, 0x41, 0x61);

    private readonly RiverWindowManager _wm;
    private readonly string _terminal;
    private readonly bool _trace;
    private readonly BasinLogger _log;
    private readonly List<Tile> _tiles = [];
    private readonly List<Tile> _onOutput = [];
    private readonly List<KeyBinding> _bindings = [];

    private WmWindow? _focused;
    private WmWindow? _fullscreen;
    private WmOutput? _fullscreenOutput;
    private double _masterFraction = 0.55;
    private bool _bindingsArmed;
    private bool _layerDefaultSet;
    private Command _pending;

    internal Tiler(RiverWindowManager wm, string terminal, bool trace, BasinLogger log)
    {
        _wm = wm;
        _terminal = terminal;
        _trace = trace;
        _log = log;
        wm.Manage += OnManage;
        wm.Render += OnRender;
    }

    private void OnManage(ManageContext context)
    {
        ArmBindings(context);
        AdoptNewWindows(context);
        ForgetClosedWindows(context);
        RunPendingCommand(context);
        Layout(context);
        ApplyFocus(context);
        Trace(context);
    }

    private void Trace(ManageContext context)
    {
        if (!_trace)
        {
            return;
        }

        var pointer = context.Seats.Count > 0 ? context.Seats[0].PointerPosition : default;
        _log.Debug($"manage: {context.Windows.Count} window(s), {context.Outputs.Count} output(s), {context.NewWindows.Count} new, {context.ClosedWindows.Count} closed, pointer {pointer.X},{pointer.Y}");
        foreach (var tile in _tiles)
        {
            var window = tile.Window;
            _log.Debug($"  {(ReferenceEquals(window, _focused) ? '*' : ' ')} '{(window.AppId ?? "?")}' proposed {tile.Frame.Width}x{tile.Frame.Height} at {tile.Frame.X},{tile.Frame.Y}; window reports {window.Dimensions.Width}x{window.Dimensions.Height}");
        }
    }

    private void OnRender(RenderContext context)
    {
        foreach (var tile in _tiles)
        {
            var window = tile.Window;
            if (ReferenceEquals(window, _fullscreen))
            {
                window.Node.PlaceTop();
                continue;
            }

            window.Node.SetPosition(tile.Frame.X, tile.Frame.Y);
            window.SetBorders(
                Edges.All,
                BorderWidth,
                ReferenceEquals(window, _focused) ? FocusedBorder : UnfocusedBorder);
        }

        if (_fullscreen is not null)
        {
            _fullscreen.Node.PlaceTop();
        }
    }

    private void ArmBindings(ManageContext context)
    {
        if (_bindingsArmed || context.Seats.Count == 0 || !_wm.Bindings.IsSupported)
        {
            return;
        }

        _bindingsArmed = true;
        var seat = context.Seats[0];
        Bind(seat, "Return", Modifiers.Super, Command.Spawn);
        Bind(seat, "j", Modifiers.Super, Command.FocusNext);
        Bind(seat, "k", Modifiers.Super, Command.FocusPrevious);
        Bind(seat, "q", Modifiers.Super, Command.CloseFocused);
        Bind(seat, "h", Modifiers.Super, Command.ShrinkMaster);
        Bind(seat, "l", Modifiers.Super, Command.GrowMaster);
        Bind(seat, "e", Modifiers.Super | Modifiers.Shift, Command.ExitSession);

        seat.WindowInteraction += window =>
        {
            if (_trace)
            {
                _log.Debug($"interaction: '{(window.AppId ?? "?")}' {window.Dimensions.Width}x{window.Dimensions.Height}");
            }

            Focus(window);
        };
    }

    private void Bind(WmSeat seat, string keysym, Modifiers modifiers, Command command)
    {
        var binding = _wm.Bindings.Bind(seat, keysym, modifiers, () => _pending = command);
        binding.Enable();
        _bindings.Add(binding);
    }

    private void AdoptNewWindows(ManageContext context)
    {
        foreach (var window in context.NewWindows)
        {
            window.SetCapabilities(WindowCapabilities.Fullscreen | WindowCapabilities.Maximize);
            window.UseServerSideDecorations();

            var output = OutputFor(context, window);
            _tiles.Add(new Tile(window, output));
            _focused ??= window;

            window.FullscreenRequested += requested => EnterFullscreen(window, requested ?? output);
            window.ExitFullscreenRequested += () => LeaveFullscreen(window);
            window.MaximizeRequested += () => window.InformMaximized(true);
            window.UnmaximizeRequested += () => window.InformMaximized(false);
            window.PointerMoveRequested += _ => { };
            window.PointerResizeRequested += (_, _) => { };
        }
    }

    private void ForgetClosedWindows(ManageContext context)
    {
        foreach (var window in context.ClosedWindows)
        {
            _tiles.RemoveAll(tile => ReferenceEquals(tile.Window, window));
            if (ReferenceEquals(_focused, window))
            {
                _focused = _tiles.Count > 0 ? _tiles[0].Window : null;
            }

            if (ReferenceEquals(_fullscreen, window))
            {
                _fullscreen = null;
                _fullscreenOutput = null;
            }
        }
    }

    private void RunPendingCommand(ManageContext context)
    {
        var command = _pending;
        _pending = Command.None;
        switch (command)
        {
            case Command.Spawn:
                Spawn();
                break;
            case Command.FocusNext:
                CycleFocus(1);
                break;
            case Command.FocusPrevious:
                CycleFocus(-1);
                break;
            case Command.CloseFocused:
                _focused?.Close();
                break;
            case Command.ShrinkMaster:
                _masterFraction = Math.Max(0.2, _masterFraction - 0.05);
                break;
            case Command.GrowMaster:
                _masterFraction = Math.Min(0.8, _masterFraction + 0.05);
                break;
            case Command.ExitSession:
                _wm.ExitSession();
                break;
        }
    }

    private void Layout(ManageContext context)
    {
        if (_wm.LayerShell is { } layerShell)
        {
            foreach (var output in context.Outputs)
            {
                layerShell.Track(output);
            }

            foreach (var seat in context.Seats)
            {
                layerShell.Track(seat);
            }

            if (context.Outputs.Count > 0 && !_layerDefaultSet)
            {
                _layerDefaultSet = true;
                context.Outputs[0].SetDefaultForLayerSurfaces();
            }
        }

        foreach (var output in context.Outputs)
        {
            LayoutOutput(output);
        }

        if (_fullscreen is not null && _fullscreenOutput is { IsRemoved: true })
        {
            LeaveFullscreen(_fullscreen);
        }
    }

    private void LayoutOutput(WmOutput output)
    {
        _onOutput.Clear();
        foreach (var tile in _tiles)
        {
            if (ReferenceEquals(tile.Output, output) && !ReferenceEquals(tile.Window, _fullscreen))
            {
                _onOutput.Add(tile);
            }
        }

        if (_onOutput.Count == 0)
        {
            return;
        }

        var area = output.NonExclusiveArea.IsEmpty ? output.Area : output.NonExclusiveArea;
        area = new Rect(
            area.X + Gap,
            area.Y + Gap,
            Math.Max(0, area.Width - (Gap * 2)),
            Math.Max(0, area.Height - (Gap * 2)));

        if (_onOutput.Count == 1)
        {
            Place(_onOutput[0], area);
            return;
        }

        var masterWidth = (int)(area.Width * _masterFraction) - (Gap / 2);
        var stackWidth = area.Width - masterWidth - Gap;
        Place(_onOutput[0], new Rect(area.X, area.Y, masterWidth, area.Height));

        var stackCount = _onOutput.Count - 1;
        var stackX = area.X + masterWidth + Gap;
        var slot = (area.Height - (Gap * (stackCount - 1))) / stackCount;
        for (var i = 0; i < stackCount; i++)
        {
            var y = area.Y + (i * (slot + Gap));
            var height = i == stackCount - 1 ? area.Bottom - y : slot;
            Place(_onOutput[i + 1], new Rect(stackX, y, stackWidth, height));
        }
    }

    private void Place(Tile tile, Rect frame)
    {
        var content = new Rect(
            frame.X + BorderWidth,
            frame.Y + BorderWidth,
            Math.Max(1, frame.Width - (BorderWidth * 2)),
            Math.Max(1, frame.Height - (BorderWidth * 2)));

        tile.Frame = content;
        tile.Window.SetTiled(Edges.All);
        tile.Window.ProposeDimensions(content.Size);
    }

    private void ApplyFocus(ManageContext context)
    {
        if (context.Seats.Count == 0)
        {
            return;
        }

        var seat = context.Seats[0];
        if (_wm.LayerShell?.HasExclusiveFocus(seat) == true)
        {
            return;
        }

        if (_focused is { IsClosed: false })
        {
            seat.FocusWindow(_focused);
        }
        else
        {
            seat.ClearFocus();
        }
    }

    private void Focus(WmWindow window)
    {
        if (!window.IsClosed)
        {
            _focused = window;
        }
    }

    private void CycleFocus(int direction)
    {
        if (_tiles.Count == 0)
        {
            return;
        }

        var index = _focused is null ? -1 : _tiles.FindIndex(tile => ReferenceEquals(tile.Window, _focused));
        index = ((index + direction) % _tiles.Count + _tiles.Count) % _tiles.Count;
        _focused = _tiles[index].Window;
    }

    private void EnterFullscreen(WmWindow window, WmOutput output)
    {
        _fullscreen = window;
        _fullscreenOutput = output;
        window.Fullscreen(output);
        window.InformFullscreen(true);
        _focused = window;
    }

    private void LeaveFullscreen(WmWindow window)
    {
        if (!ReferenceEquals(_fullscreen, window))
        {
            return;
        }

        _fullscreen = null;
        _fullscreenOutput = null;
        window.ExitFullscreen();
        window.InformFullscreen(false);

        var tile = _tiles.Find(t => ReferenceEquals(t.Window, window));
        if (tile is not null)
        {
            LayoutOutput(tile.Output);
        }
    }

    private WmOutput OutputFor(ManageContext context, WmWindow window)
    {
        if (window.Parent is { } parent)
        {
            var parentTile = _tiles.Find(tile => ReferenceEquals(tile.Window, parent));
            if (parentTile is not null)
            {
                return parentTile.Output;
            }
        }

        if (context.Seats.Count > 0)
        {
            var pointer = context.Seats[0].PointerPosition;
            foreach (var output in context.Outputs)
            {
                if (output.Area.Contains(pointer))
                {
                    return output;
                }
            }
        }

        return context.Outputs.Count > 0
            ? context.Outputs[0]
            : throw new InvalidOperationException("a window appeared before any output did");
    }

    private void Spawn()
    {
        if (WmSpawn.Run(_terminal) is { } failure)
        {
            _log.Error($"could not spawn '{_terminal}': {failure}");
        }
    }

    private sealed class Tile(WmWindow window, WmOutput output)
    {
        public WmWindow Window { get; } = window;

        public WmOutput Output { get; } = output;

        public Rect Frame { get; set; }
    }

    private enum Command
    {
        None,
        Spawn,
        FocusNext,
        FocusPrevious,
        CloseFocused,
        ShrinkMaster,
        GrowMaster,
        ExitSession,
    }
}
