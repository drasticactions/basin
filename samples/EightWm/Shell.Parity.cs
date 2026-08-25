using Basin.Seat;
using Xkb;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    internal const int HotCornerSize = 6;
    internal const int CornerSlideDistance = 60;
    internal const int CornerBand = 48;

    private static readonly XkbKeysym KeyTab = XkbKeysym.FromName("Tab");
    private static readonly XkbKeysym KeyC = XkbKeysym.FromName("c");
    private static readonly XkbKeysym KeyZ = XkbKeysym.FromName("z");
    private static readonly XkbKeysym KeyQ = XkbKeysym.FromName("q");
    private static readonly XkbKeysym KeyS = XkbKeysym.FromName("s");
    private static readonly XkbKeysym KeyH = XkbKeysym.FromName("h");
    private static readonly XkbKeysym KeyK = XkbKeysym.FromName("k");
    private static readonly XkbKeysym KeyI = XkbKeysym.FromName("i");
    private static readonly XkbKeysym KeyPeriod = XkbKeysym.FromName("period");
    private static readonly XkbKeysym KeyGreater = XkbKeysym.FromName("greater");
    private static readonly XkbKeysym KeyF4 = XkbKeysym.FromName("F4");
    private static readonly XkbKeysym KeyEscape = XkbKeysym.FromName("Escape");
    private static readonly XkbKeysym KeyBackspace = XkbKeysym.FromName("BackSpace");
    private static readonly XkbKeysym KeySuperLeft = XkbKeysym.FromName("Super_L");
    private static readonly XkbKeysym KeySuperRight = XkbKeysym.FromName("Super_R");

    private bool _superPressed;
    private bool _superUsed;
    private bool _switcherHeld;

    internal bool HandleSuperRelease(ShellView view, XkbKeysym symbol)
    {
        if (symbol != KeySuperLeft && symbol != KeySuperRight)
        {
            return false;
        }

        var tapped = _superPressed && !_superUsed;
        _superPressed = false;
        _superUsed = false;
        _switcherHeld = false;
        if (tapped)
        {
            ToggleStart(view);
        }

        return true;
    }

    internal bool HandleChord(ShellView view, XkbKeysym symbol, bool super, bool shift, bool alt, bool control)
    {
        if (symbol == KeySuperLeft || symbol == KeySuperRight)
        {
            if (!_superPressed)
            {
                _superUsed = false;
            }

            _superPressed = true;
            return true;
        }

        if (_superPressed)
        {
            _superUsed = true;
        }

        if (alt && symbol == KeyF4)
        {
            CloseFocused();
            return true;
        }

        if (!super)
        {
            return control && HandleZoomKeys(view, symbol) || TypeOnStart(view, symbol, shift);
        }

        if (symbol == KeyTab)
        {
            if (_switcherHeld)
            {
                DockSwitcher(view, true);
            }
            else
            {
                _switcherHeld = true;
                DockSwitcher(view, false);
                SwitchToPrevious(view);
            }

            return true;
        }

        if (symbol == KeyC)
        {
            ToggleCharms(view);
            return true;
        }

        if (symbol == KeyZ)
        {
            ToggleTitle(view);
            return true;
        }

        if (symbol == KeyPeriod || symbol == KeyGreater)
        {
            if (Focused is { } app)
            {
                SnapWithin(view, app, shift ? 0 : view.Host.SlotCount);
            }

            return true;
        }

        var charm = symbol switch
        {
            _ when symbol == KeyQ || symbol == KeyS => Charm.Search,
            _ when symbol == KeyH => Charm.Share,
            _ when symbol == KeyK => Charm.Devices,
            _ when symbol == KeyI => Charm.Settings,
            _ => Charm.None,
        };

        if (charm == Charm.None)
        {
            return false;
        }

        ShowCharms(view, true);
        ActivateCharm(view, charm);
        return true;
    }

    private bool HandleZoomKeys(ShellView view, XkbKeysym symbol)
    {
        if (symbol == ZoomIn || symbol == ZoomEqual)
        {
            ToggleZoom(view, zoomOut: false);
            return true;
        }

        if (symbol == ZoomOut)
        {
            ToggleZoom(view, zoomOut: true);
            return true;
        }

        return false;
    }

    private static readonly XkbKeysym ZoomIn = XkbKeysym.FromName("plus");
    private static readonly XkbKeysym ZoomEqual = XkbKeysym.FromName("equal");
    private static readonly XkbKeysym ZoomOut = XkbKeysym.FromName("minus");

    private readonly System.Text.StringBuilder _filter = new();

    internal string Filter => _filter.ToString();

    private bool TypeOnStart(ShellView view, XkbKeysym symbol, bool shift)
    {
        if (view.Start is not { } start || !view.Background.Enabled)
        {
            return false;
        }

        if (symbol == KeyEscape)
        {
            if (_filter.Length == 0)
            {
                return false;
            }

            _filter.Clear();
            ApplyFilter(view, start);
            return true;
        }

        if (symbol == KeyBackspace)
        {
            if (_filter.Length == 0)
            {
                return false;
            }

            _filter.Length--;
            ApplyFilter(view, start);
            return true;
        }

        var text = symbol.ToUtf8String();
        if (text.Length != 1 || char.IsControl(text[0]))
        {
            return false;
        }

        _ = shift;
        _filter.Append(text);
        ApplyFilter(view, start);
        return true;
    }

    private void ApplyFilter(ShellView view, StartScreen start)
    {
        var text = _filter.ToString();
        if (text.Length == 0)
        {
            ShowApps(view, false);
            BasinReport.Line($"FILTER none");
            return;
        }

        var matches = new List<DesktopEntry>();
        foreach (var entry in _entries)
        {
            if (entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(entry);
            }
        }

        start.SetApps(matches);
        start.AppsVisible = true;
        start.AppsPan.Reset(0);
        BasinReport.Line($"FILTER {text} matches={matches.Count}");
    }

    internal HotCorner CornerAt(ShellView view, double localX, double localY)
    {
        if (!HotCornersOn)
        {
            return HotCorner.None;
        }

        _ = view;
        const double size = HotCornerSize;
        var left = localX <= size;
        var right = localX >= view.Box.Width - size;
        var top = localY <= size;
        var bottom = localY >= view.Box.Height - size;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => HotCorner.TopLeft,
            (_, true, true, _) => HotCorner.TopRight,
            (true, _, _, true) => HotCorner.BottomLeft,
            (_, true, _, true) => HotCorner.BottomRight,
            _ => HotCorner.None,
        };
    }

    private HotCorner _corner;
    private double _cornerY;

    internal void TrackCorner(ShellView view, double localX, double localY)
    {
        if (!HotCornersOn)
        {
            return;
        }

        if (CornerAt(view, localX, localY) is var corner && corner != HotCorner.None)
        {
            if (corner != _corner)
            {
                _corner = corner;
                _cornerY = localY;
            }

            return;
        }

        if (_corner == HotCorner.None)
        {
            return;
        }

        var onLeft = _corner is HotCorner.TopLeft or HotCorner.BottomLeft;
        const double band = CornerBand;
        if (onLeft ? localX > band : localX < view.Box.Width - band)
        {
            _corner = HotCorner.None;
            return;
        }

        var travel = _corner is HotCorner.TopLeft or HotCorner.TopRight
            ? localY - _cornerY
            : _cornerY - localY;
        if (travel < CornerSlideDistance)
        {
            return;
        }

        var from = _corner;
        _corner = HotCorner.None;
        BasinReport.Line($"CORNER {from} slide");
        if (onLeft)
        {
            DockSwitcher(view, true);
        }
        else
        {
            ShowCharms(view, true);
        }
    }

    internal bool CornerClick(ShellView view, double localX, double localY)
    {
        switch (CornerAt(view, localX, localY))
        {
            case HotCorner.TopLeft:
                SwitchToPrevious(view);
                return true;

            case HotCorner.BottomLeft:
                ToggleStart(view);
                return true;

            default:
                return false;
        }
    }
}
