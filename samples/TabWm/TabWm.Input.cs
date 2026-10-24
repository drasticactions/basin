using Basin;
using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Shell.Xdg;

namespace TabWm;

internal sealed partial class TabWm
{
    private const long DoubleClickMs = 400;

    private readonly HashSet<uint> _swallowed = [];
    private long _lastTitleClick;
    private TabWindow? _lastTitleWindow;

    private Modifiers Held => HeldModifiers.From(Seat.Keyboard.State);

    private bool IsModifierKey(uint key) =>
        Seat.Keyboard.RawKeysymFor(key).Value is (>= 0xffe1 and <= 0xffee) or (>= 0xfe01 and <= 0xfe0f) or 0xff7e;

    private bool OnKey(uint time, uint key, bool pressed)
    {
        if (IsModifierKey(key))
        {
            _host.Loop.AddIdle(UpdatePeek);
            return false;
        }

        if (!pressed)
        {
            return _swallowed.Remove(key);
        }

        if (_layers.IsLocked)
        {
            return false;
        }

        if (key == InputCodes.KeyEsc && _overlay is not null)
        {
            CloseOverlay();
            _swallowed.Add(key);
            return true;
        }

        var symbol = Seat.Keyboard.RawKeysymFor(key).ToLower().Value;
        var held = Held;
        foreach (var binding in _config.Bindings)
        {
            if (binding.Keysym == symbol && binding.Modifiers == held)
            {
                _swallowed.Add(key);
                Run(binding);
                return true;
            }
        }

        if (KeyboardTarget is null && _focused is { } focused)
        {
            focused.HadInput = true;
        }

        return false;
    }

    private void UpdatePeek()
    {
        var super = (Held & Modifiers.Super) != 0;
        foreach (var set in _sets)
        {
            var peek = super && ReferenceEquals(set, FocusedSet) && set.Active?.HasFullscreen == true;
            if (set.StripPeek != peek)
            {
                set.StripPeek = peek;
                UpdateStripVisibility(set);
                BasinReport.Line($"PEEK output={set.Output.Name} {(peek ? "on" : "off")}");
                _outputs.ScheduleAll();
            }
        }
    }

    private void Run(TabBinding binding)
    {
        var set = FocusedSet;
        var tab = set?.Active;
        switch (binding.Action)
        {
            case TabAction.NewTab when set is not null:
                NewTab(set, select: true);
                break;
            case TabAction.CloseTab when tab is not null:
                CloseTab(tab);
                break;
            case TabAction.NextTab:
                SelectRelative(1);
                break;
            case TabAction.PreviousTab:
                SelectRelative(-1);
                break;
            case TabAction.SelectTab when set is not null && binding.Argument - 1 < set.Tabs.Count:
                SelectTab(set.Tabs[binding.Argument - 1]);
                break;
            case TabAction.LastTab when set is { Tabs.Count: > 0 }:
                SelectTab(set.Tabs[^1]);
                break;
            case TabAction.Launcher when set is not null:
                if (_overlay is null)
                {
                    ShowOverlay(set);
                }
                else
                {
                    CloseOverlay();
                }

                break;
            case TabAction.MoveWindowToTab when _focused is { Tab.Set: { } own } window && binding.Argument - 1 < own.Tabs.Count:
                MoveWindow(window, own.Tabs[binding.Argument - 1], follow: false);
                break;
            case TabAction.MoveWindowToPreviousTab when _focused is { Tab: { } current } window:
                MoveWindowBy(window, current, -1);
                break;
            case TabAction.MoveWindowToNextTab when _focused is { Tab: { } current } window:
                MoveWindowBy(window, current, 1);
                break;
            case TabAction.MoveTabLeft when tab is not null:
                MoveTabToNeighbor(tab, -1);
                break;
            case TabAction.MoveTabRight when tab is not null:
                MoveTabToNeighbor(tab, 1);
                break;
            case TabAction.CloseWindow:
                _focused?.Close();
                break;
            case TabAction.Fullscreen when _focused is { } window:
                SetFullscreen(window, !window.Fullscreen);
                break;
            case TabAction.Maximize when _focused is { } window:
                SetMaximized(window, !window.Maximized);
                break;
            case TabAction.Minimize when _focused is { } window:
                Minimize(window);
                break;
            case TabAction.MuteTab when tab is not null:
                SetMuted(tab, !tab.Muted);
                break;
            case TabAction.Lock:
                Lock();
                break;
            case TabAction.ScreenReader:
                _access?.Run("screen-reader");
                break;
            case TabAction.Magnifier:
                _access?.Run("magnifier");
                break;
            case TabAction.MagnifierIn:
                _access?.Run("magnifier-in");
                break;
            case TabAction.MagnifierOut:
                _access?.Run("magnifier-out");
                break;
            case TabAction.ScreenCurtain:
                _access?.Run("screen-curtain");
                break;
            case TabAction.Exec when binding.Command is { Length: > 0 } command:
                Launch(tab, Basin.Freedesktop.ExecLine.Join(command), null, null);
                break;
        }
    }

    private void MoveWindowBy(TabWindow window, Tab current, int step)
    {
        var set = current.Set;
        var index = set.IndexOf(current) + step;
        if (index >= 0 && index < set.Tabs.Count)
        {
            MoveWindow(window, set.Tabs[index], follow: false);
        }
    }

    private void MoveTabToNeighbor(Tab tab, int direction)
    {
        var ordered = _sets.OrderBy(set => set.Box.X).ThenBy(set => set.Box.Y).ToList();
        var index = ordered.IndexOf(tab.Set) + direction;
        if (index < 0 || index >= ordered.Count)
        {
            return;
        }

        var target = ordered[index];
        MoveTab(tab, target, target.Tabs.Count, select: true);
    }

    private bool OnButton(uint time, uint button, bool pressed)
    {
        if (!pressed)
        {
            if (_grab.Active)
            {
                EndGrab(time, button);
                return true;
            }

            return false;
        }

        if (_grab.Active || _seat is not { } seat)
        {
            return true;
        }

        var x = seat.PointerX;
        var y = seat.PointerY;
        if (_overlay is { } overlay && !overlay.Contains(x, y))
        {
            CloseOverlay();
            return true;
        }

        var window = WindowUnder(x, y);
        if (window is not null && (Held & Modifiers.Super) != 0)
        {
            Focus(window);
            switch (button)
            {
                case InputCodes.BtnLeft:
                    BeginMove(window, x, y);
                    return true;
                case InputCodes.BtnRight:
                    BeginResize(window, QuadrantEdges(window, x, y), x, y);
                    return true;
            }
        }

        if (window is not null)
        {
            window.HadInput = true;
            if (!ReferenceEquals(window, _focused) || KeyboardTarget is not null)
            {
                Focus(window);
            }
        }

        return false;
    }

    private static ResizeEdges QuadrantEdges(TabWindow window, double x, double y)
    {
        var geometry = window.Geometry;
        var edges = x < geometry.X + (geometry.Width / 2) ? ResizeEdges.Left : ResizeEdges.Right;
        edges |= y < geometry.Y + (geometry.Height / 2) ? ResizeEdges.Top : ResizeEdges.Bottom;
        return edges;
    }

    private TabWindow? WindowUnder(double x, double y) =>
        _scene.SurfaceAt(x, y) is { Surface: { } surface } ? WindowOwning(surface) : null;

    private bool OwnsFrame(IUISurface surface) => FrameOwning(surface) is not null;

    private TabWindow? FrameOwning(IUISurface surface)
    {
        foreach (var window in _windows)
        {
            if (window.Frame?.OwnsSurface(surface) == true)
            {
                return window;
            }
        }

        return null;
    }

    private bool OnFrameButton(double x, double y, uint button, bool pressed)
    {
        if (_grab.Active || !pressed || _seat?.Hovered is not { } hovered || FrameOwning(hovered) is not { Frame: { } frame } window)
        {
            return false;
        }

        Focus(window);
        if (button != InputCodes.BtnLeft)
        {
            return frame.HitsTitlebar(x, y);
        }

        var edges = (ResizeEdges)frame.EdgeAt(x, y);
        if (edges != ResizeEdges.None)
        {
            BeginResize(window, edges, x, y);
            return true;
        }

        if (!frame.HitsTitlebar(x, y))
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (ReferenceEquals(_lastTitleWindow, window) && now - _lastTitleClick < DoubleClickMs)
        {
            _lastTitleClick = 0;
            SetMaximized(window, !window.Maximized);
            return true;
        }

        _lastTitleClick = now;
        _lastTitleWindow = window;
        BeginMove(window, x, y);
        return true;
    }

    private string? FrameCursorName(double x, double y)
    {
        if (_seat?.Hovered is not { } hovered || FrameOwning(hovered)?.Frame is not { } frame)
        {
            return null;
        }

        return (ResizeEdges)frame.EdgeAt(x, y) switch
        {
            ResizeEdges.Left => "w-resize",
            ResizeEdges.Right => "e-resize",
            ResizeEdges.Top => "n-resize",
            ResizeEdges.Bottom => "s-resize",
            ResizeEdges.TopLeft => "nw-resize",
            ResizeEdges.TopRight => "ne-resize",
            ResizeEdges.BottomLeft => "sw-resize",
            ResizeEdges.BottomRight => "se-resize",
            _ => null,
        };
    }
}
