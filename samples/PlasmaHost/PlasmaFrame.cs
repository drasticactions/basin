using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Styling;
using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using PlasmaHost.Shell;

namespace PlasmaHost;

internal sealed class PlasmaFrame : IDisposable
{
    private const uint DoubleClickMs = 400;

    private readonly AvaloniaUIHost _host;
    private readonly BreezeTheme _theme;
    private readonly BreezeIcons _icons;
    private readonly UISurfaceIndex? _index;
    private readonly SceneTree _tree;
    private readonly BreezeTitleModel _title = new();
    private readonly BreezeEdgeModel[] _edges = [new(), new(), new()];
    private readonly Strip[] _strips = new Strip[4];

    private Box _geometry;
    private FrameInsets _insets;
    private FrameState _state;
    private BreezeButtons _buttons;
    private double _scale = 1.0;
    private bool _valid;
    private bool _disposed;
    private FramePart _hot;
    private FramePart _pressed;
    private bool _titleArmed;
    private uint _lastTitlePressMs;
    private int _touchLatch = -1;
    private ThemeVariant _variant = ThemeVariant.Light;
    private MenuFlyout? _menu;

    public PlasmaFrame(
        AvaloniaUIHost host, BreezeTheme theme, BreezeIcons icons, SceneTree parent, UISurfaceIndex? index)
    {
        _host = host;
        _theme = theme;
        _icons = icons;
        _index = index;
        _tree = new SceneTree(parent);
    }

    public event Action<FrameAction>? Requested;

    public FrameInsets Insets => _insets;

    public Box OuterBounds { get; private set; }

    public double Scale => _scale;

    public double TouchSlop { get; set; }

    public bool IsMenuOpen => _menu is { IsOpen: true };

    public bool Visible
    {
        get => _tree.Enabled;
        set => _tree.Enabled = value;
    }

    public FrameInsets Measure(in FrameState state, double scale) => _theme.Metrics.InsetsOf();

    public void Configure(in Box geometry, double scale, in FrameState state)
    {
        if (_disposed || geometry.IsEmpty || scale <= 0)
        {
            return;
        }

        var metrics = _theme.Metrics;
        var insets = metrics.InsetsOf();
        var outer = new Box(
            geometry.X - insets.Left,
            geometry.Y - insets.Top,
            geometry.Width + insets.Left + insets.Right,
            geometry.Height + insets.Top + insets.Bottom);

        _geometry = geometry;
        _insets = insets;
        _state = state;
        _scale = scale;
        OuterBounds = outer;
        _valid = true;
        _tree.SetPosition(outer.X, outer.Y);

        _buttons = metrics.LayoutButtons(outer.Width, state.Capabilities);
        ApplyState(outer.Width);

        Place(0, new Box(0, 0, outer.Width, insets.Top), () => new BreezeTitleView { DataContext = _title });
        Place(
            1,
            new Box(0, insets.Top, insets.Left, geometry.Height),
            () => new BreezeEdgeView { DataContext = _edges[0] });
        Place(
            2,
            new Box(outer.Width - insets.Right, insets.Top, insets.Right, geometry.Height),
            () => new BreezeEdgeView { DataContext = _edges[1] });
        Place(
            3,
            new Box(0, outer.Height - insets.Bottom, outer.Width, insets.Bottom),
            () => new BreezeEdgeView { DataContext = _edges[2] });
        ApplyVariant();
    }

    public void SyncPositions()
    {
        if (_disposed || !_valid)
        {
            return;
        }

        var (originX, originY) = SceneOrigin();
        foreach (var strip in _strips)
        {
            if (strip.Surface is { } surface)
            {
                surface.SetPosition(originX + strip.Node.Node.X, originY + strip.Node.Node.Y);
            }
        }
    }

    public bool OwnsNode(SceneNode node)
    {
        for (var candidate = node; candidate is not null; candidate = candidate.Parent)
        {
            if (candidate == _tree)
            {
                return true;
            }
        }

        return false;
    }

    public bool OwnsSurface(IUISurface surface)
    {
        foreach (var strip in _strips)
        {
            if (strip.Surface is { } candidate && ReferenceEquals(candidate, surface))
            {
                return true;
            }
        }

        return false;
    }

    public FramePart PartAt(double x, double y)
    {
        if (!_valid || _disposed || !_tree.Enabled)
        {
            return FramePart.None;
        }

        var sx = x - OuterBounds.X;
        var sy = y - OuterBounds.Y;
        var width = OuterBounds.Width;
        var height = OuterBounds.Height;
        if (sx < 0 || sy < 0 || sx >= width || sy >= height)
        {
            return FramePart.None;
        }

        if (sx >= _insets.Left && sx < _insets.Left + _geometry.Width &&
            sy >= _insets.Top && sy < _insets.Top + _geometry.Height)
        {
            return FramePart.None;
        }

        if (!_state.Maximized && !_state.Fullscreen)
        {
            if (sy < BreezeMetrics.ResizeBand)
            {
                return sx < BreezeMetrics.CornerZone ? FramePart.TopLeft
                    : sx >= width - BreezeMetrics.CornerZone ? FramePart.TopRight
                    : FramePart.Top;
            }

            if (sx < BreezeMetrics.ResizeBand && sy < BreezeMetrics.CornerZone)
            {
                return FramePart.TopLeft;
            }

            if (sx >= width - BreezeMetrics.ResizeBand && sy < BreezeMetrics.CornerZone)
            {
                return FramePart.TopRight;
            }
        }

        if (sy < BreezeMetrics.TitleHeight)
        {
            var button = _buttons.PartAt(sx, sy);
            return button == FramePart.None ? FramePart.Title : button;
        }

        var border = _theme.Metrics.BorderWidth;
        if (border > 0)
        {
            var nearBottom = sy >= height - BreezeMetrics.CornerZone;
            if (sx < border)
            {
                return nearBottom ? FramePart.BottomLeft : FramePart.Left;
            }

            if (sx >= width - border)
            {
                return nearBottom ? FramePart.BottomRight : FramePart.Right;
            }

            if (sy >= height - border)
            {
                return sx < BreezeMetrics.CornerZone ? FramePart.BottomLeft
                    : sx >= width - BreezeMetrics.CornerZone ? FramePart.BottomRight
                    : FramePart.Bottom;
            }
        }

        return FramePart.Border;
    }

    public string? CursorAt(double x, double y) => PartAt(x, y) switch
    {
        FramePart.Top => "top_side",
        FramePart.Bottom => "bottom_side",
        FramePart.Left => "left_side",
        FramePart.Right => "right_side",
        FramePart.TopLeft => "top_left_corner",
        FramePart.TopRight => "top_right_corner",
        FramePart.BottomLeft => "bottom_left_corner",
        FramePart.BottomRight => "bottom_right_corner",
        _ => null,
    };

    public void PointerEnter(double x, double y) => PointerMotion(x, y);

    public void PointerMotion(double x, double y) => SetHot(PartAt(x, y));

    public void PointerLeave()
    {
        SetHot(FramePart.None);
        SetPressed(FramePart.None);
    }

    public void PointerButton(double x, double y, bool pressed, uint timeMs = 0)
    {
        if (_touchLatch >= 0)
        {
            return;
        }

        var part = PartAt(x, y);
        if (pressed)
        {
            Press(part, x, y, timeMs);
            return;
        }

        var released = _pressed;
        if (released == FramePart.None)
        {
            return;
        }

        SetPressed(FramePart.None);
        if (part == released)
        {
            Activate(released, x, y);
        }
    }

    public void TouchDown(double x, double y, int id, uint timeMs = 0)
    {
        if (_touchLatch >= 0 || _pressed != FramePart.None)
        {
            return;
        }

        var part = TouchPartAt(x, y);
        if (part is FramePart.Close or FramePart.Maximize or FramePart.Minimize or FramePart.Menu or FramePart.Icon)
        {
            _touchLatch = id;
        }

        Press(part, x, y, timeMs);
    }

    public void TouchUp(double x, double y, int id)
    {
        if (_touchLatch != id)
        {
            return;
        }

        _touchLatch = -1;
        var released = _pressed;
        if (released == FramePart.None)
        {
            return;
        }

        SetPressed(FramePart.None);
        if (TouchPartAt(x, y) == released)
        {
            Activate(released, x, y);
        }
    }

    public void TouchCancel()
    {
        if (_touchLatch < 0)
        {
            return;
        }

        _touchLatch = -1;
        SetPressed(FramePart.None);
    }

    public void OpenMenu(double x, double y)
    {
        if (_disposed || !_valid || _strips[0].Surface?.Content is not Control anchor)
        {
            return;
        }

        DismissMenu();
        var flyout = new MenuFlyout
        {
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            HorizontalOffset = x - OuterBounds.X,
            VerticalOffset = y - OuterBounds.Y,
        };

        foreach (var label in MenuLabels())
        {
            var item = new MenuItem { Header = label };
            var action = ActionFor(label);
            item.Click += (_, _) =>
            {
                DismissMenu();
                Requested?.Invoke(action);
            };
            flyout.Items.Add(item);
        }

        if (flyout.Items.Count == 0)
        {
            return;
        }

        _menu = flyout;
        flyout.ShowAt(anchor);
    }

    public void DismissMenu()
    {
        if (_menu is not { } flyout)
        {
            return;
        }

        _menu = null;
        flyout.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DismissMenu();
        for (var i = 0; i < _strips.Length; i++)
        {
            _strips[i].Node?.Dispose();
            _strips[i].Surface?.Dispose();
            _strips[i] = default;
        }

        _tree.Destroy();
    }

    private IEnumerable<string> MenuLabels()
    {
        if (_state.Capabilities.HasFlag(FrameCapabilities.Minimize))
        {
            yield return "Minimize";
        }

        if (_state.Capabilities.HasFlag(FrameCapabilities.Maximize))
        {
            yield return _state.Maximized ? "Restore" : "Maximize";
        }

        yield return "Close";
    }

    private static FrameAction ActionFor(string label) => label switch
    {
        "Minimize" => new FrameAction(FrameActionKind.Minimize),
        "Close" => new FrameAction(FrameActionKind.Close),
        _ => new FrameAction(FrameActionKind.ToggleMaximize),
    };

    private void Press(FramePart part, double x, double y, uint timeMs)
    {
        switch (part)
        {
            case FramePart.Close:
            case FramePart.Maximize:
            case FramePart.Minimize:
            case FramePart.Menu:
            case FramePart.Icon:
                _titleArmed = false;
                SetPressed(part);
                return;
            case FramePart.Title:
                if (_titleArmed && timeMs != 0 && timeMs - _lastTitlePressMs <= DoubleClickMs)
                {
                    _titleArmed = false;
                    Requested?.Invoke(new FrameAction(FrameActionKind.ToggleMaximize));
                    return;
                }

                _titleArmed = true;
                _lastTitlePressMs = timeMs;
                Requested?.Invoke(new FrameAction(FrameActionKind.Move));
                return;
            case FramePart.Border:
                _titleArmed = false;
                Requested?.Invoke(new FrameAction(FrameActionKind.Move));
                return;
            case FramePart.None:
                return;
            default:
                _titleArmed = false;
                Requested?.Invoke(new FrameAction(FrameActionKind.Resize, EdgesOf(part)));
                return;
        }
    }

    private void Activate(FramePart part, double x, double y)
    {
        switch (part)
        {
            case FramePart.Close:
                Requested?.Invoke(new FrameAction(FrameActionKind.Close));
                break;
            case FramePart.Maximize:
                Requested?.Invoke(new FrameAction(FrameActionKind.ToggleMaximize));
                break;
            case FramePart.Minimize:
                Requested?.Invoke(new FrameAction(FrameActionKind.Minimize));
                break;
            case FramePart.Menu:
            case FramePart.Icon:
                OpenMenu(x, y);
                break;
        }
    }

    private FramePart TouchPartAt(double x, double y)
    {
        var part = PartAt(x, y);
        if (TouchSlop <= 0 || part != FramePart.None || !_valid)
        {
            return part;
        }

        var left = (double)OuterBounds.X;
        var top = (double)OuterBounds.Y;
        var right = left + OuterBounds.Width;
        var bottom = top + OuterBounds.Height;
        if (x < left - TouchSlop || x > right + TouchSlop ||
            y < top - TouchSlop || y > bottom + TouchSlop)
        {
            return part;
        }

        var near = PartAt(Math.Clamp(x, left, right - 1), Math.Clamp(y, top, bottom - 1));
        return EdgesOf(near) != FrameEdges.None ? near : part;
    }

    public static FrameEdges EdgesOf(FramePart part) => part switch
    {
        FramePart.Top => FrameEdges.Top,
        FramePart.Bottom => FrameEdges.Bottom,
        FramePart.Left => FrameEdges.Left,
        FramePart.Right => FrameEdges.Right,
        FramePart.TopLeft => FrameEdges.TopLeft,
        FramePart.TopRight => FrameEdges.TopRight,
        FramePart.BottomLeft => FrameEdges.BottomLeft,
        FramePart.BottomRight => FrameEdges.BottomRight,
        _ => FrameEdges.None,
    };

    private void SetHot(FramePart part)
    {
        if (_hot == part)
        {
            return;
        }

        _hot = part;
        foreach (var button in _title.Buttons)
        {
            button.Hot = button.Part == part;
        }
    }

    private void SetPressed(FramePart part)
    {
        if (_pressed == part)
        {
            return;
        }

        _pressed = part;
        foreach (var button in _title.Buttons)
        {
            button.Pressed = button.Part == part;
        }
    }

    private void ApplyState(int outerWidth)
    {
        var brushes = _theme.BrushesFor(_state.Palette);
        _variant = _theme.PaletteFor(_state.Palette).IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        _title.Brushes = brushes;
        _title.Width = outerWidth;
        _title.Title = string.IsNullOrEmpty(_state.Title) ? _state.AppId ?? string.Empty : _state.Title;
        _title.Active = _state.Active;
        _title.Maximized = _state.Maximized;
        _title.TitleLeft = _buttons.TitleLeft;
        _title.TitleRight = _buttons.TitleRight;

        foreach (var edge in _edges)
        {
            edge.Brushes = brushes;
            edge.Active = _state.Active;
        }

        foreach (var button in _title.Buttons)
        {
            var box = _buttons.BoundsOf(button.Part);
            button.IsVisible = !box.IsEmpty;
            button.X = box.X;
            button.Y = box.Y;
        }

        _title.ButtonFor(FramePart.Menu).Icon = IconOf();
    }

    private void ApplyVariant()
    {
        foreach (var strip in _strips)
        {
            if (strip.Surface?.Root is { } root)
            {
                root.RequestedThemeVariant = _variant;
            }
        }
    }

    private Avalonia.Media.IImage? IconOf()
    {
        if (_state.Icon.Pixels is { } pixels && _icons.For(pixels) is { } fromPixels)
        {
            return fromPixels;
        }

        if (_state.Icon.Name is { Length: > 0 } name && _icons.For(name) is { } named)
        {
            return named;
        }

        var appId = string.IsNullOrEmpty(_state.AppId) ? _state.Title : _state.AppId;
        return appId is { Length: > 0 } ? _icons.For(appId) : null;
    }

    private void Place(int slot, in Box local, Func<Control> content)
    {
        var strip = _strips[slot];
        if (local.IsEmpty)
        {
            if (strip.Surface is not null)
            {
                strip.Node.Dispose();
                strip.Surface.Dispose();
                _strips[slot] = default;
            }

            return;
        }

        if (strip.Surface is null)
        {
            var created = _host.CreateSurface(new UISurfaceOptions
            {
                Target = _host.Produces,
                Width = local.Width,
                Height = local.Height,
                Scale = _scale,
            }) as AvaloniaUISurface;
            if (created is null)
            {
                return;
            }

            created.Content = content();
            strip = new Strip(created, new UISurfaceNode(_tree, created, _index) { PreciseDamage = true });
            _strips[slot] = strip;
        }
        else if (local.Width != strip.Width || local.Height != strip.Height || _scale != strip.Scale)
        {
            strip.Node.Configure(local.Width, local.Height, _scale);
        }

        if (strip.Surface is not { } surface)
        {
            return;
        }

        _strips[slot] = strip with { Width = local.Width, Height = local.Height, Scale = _scale };
        var (originX, originY) = SceneOrigin();
        surface.SetPosition(originX + local.X, originY + local.Y);
        strip.Node.SetPosition(local.X, local.Y);
    }

    private (int X, int Y) SceneOrigin()
    {
        var x = 0;
        var y = 0;
        for (SceneNode? node = _tree; node is not null; node = node.Parent)
        {
            x += node.X;
            y += node.Y;
        }

        return (x, y);
    }

    private readonly record struct Strip(
        AvaloniaUISurface? Surface,
        UISurfaceNode Node,
        int Width = 0,
        int Height = 0,
        double Scale = 0);
}
