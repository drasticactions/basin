using Basin.Capabilities;
using Basin.UI.Skia;
using SkiaSharp;

namespace Basin.Frames.Metacity;

public sealed partial class MetacityFrameRenderer
{
    private const int MenuPad = 3;
    private const int MenuTextInset = 26;
    private const int MenuTextRightPad = 16;
    private const int MenuItemPad = 4;
    private const int MenuSeparatorHeight = 7;
    private const int MenuMarkSide = 10;

    private enum MenuEntry
    {
        Minimize,
        Maximize,
        Unmaximize,
        Shade,
        Unshade,
        Move,
        Resize,
        Separator,
        Above,
        Stick,
        Unstick,
        Close,
    }

    private readonly MenuEntry[] _menu = new MenuEntry[12];
    private int _menuCount;
    private int _menuWidth;
    private int _menuHeight;
    private int _menuItemHeight;

    public UISurfaceSize MeasureMenu(in FrameState state, double scale)
    {
        LayoutMenu(in state);
        return new UISurfaceSize(_menuWidth, _menuHeight, scale);
    }

    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
        LayoutMenu(in state);
        var skia = (ISkiaUISurface)surface;
        var palette = _painter.Palette;
        var fill = _painter.Resources.Fill;
        var font = _painter.Font.FontFor(1.0);
        var cache = _painter.Font.CacheFor(1.0);
        var metrics = font.Metrics;
        var canvas = skia.BeginDraw();
        try
        {
            fill.Color = Color(palette.Bg(MetacityStateFlag.Normal));
            canvas.DrawRect(0, 0, _menuWidth, _menuHeight, fill);
            fill.Color = Color(palette.DarkOf(MetacityStateFlag.Normal));
            canvas.DrawRect(0, 0, _menuWidth, 1, fill);
            canvas.DrawRect(0, _menuHeight - 1, _menuWidth, 1, fill);
            canvas.DrawRect(0, 0, 1, _menuHeight, fill);
            canvas.DrawRect(_menuWidth - 1, 0, 1, _menuHeight, fill);

            var top = MenuPad;
            for (var item = 0; item < _menuCount; item++)
            {
                var entry = _menu[item];
                if (entry == MenuEntry.Separator)
                {
                    fill.Color = Color(palette.DarkOf(MetacityStateFlag.Normal));
                    canvas.DrawRect(MenuPad, top + MenuSeparatorHeight / 2, _menuWidth - 2 * MenuPad, 1, fill);
                    fill.Color = Color(palette.LightOf(MetacityStateFlag.Normal));
                    canvas.DrawRect(MenuPad, top + MenuSeparatorHeight / 2 + 1, _menuWidth - 2 * MenuPad, 1, fill);
                    top += MenuSeparatorHeight;
                    continue;
                }

                var hot = item == hotItem;
                var textState = hot ? MetacityStateFlag.Selected : MetacityStateFlag.Normal;
                if (hot)
                {
                    fill.Color = Color(palette.Bg(MetacityStateFlag.Selected));
                    canvas.DrawRect(MenuPad, top, _menuWidth - 2 * MenuPad, _menuItemHeight, fill);
                }

                fill.Color = Color(palette.Fg(textState));
                DrawMenuMark(canvas, entry, in state, top, fill);
                if (cache.TryGetBlob(Label(entry), font, out var blob, out _))
                {
                    canvas.DrawText(blob, MenuTextInset, top + MenuItemPad - metrics.Ascent, fill);
                }

                top += _menuItemHeight;
            }
        }
        finally
        {
            skia.EndDraw();
        }
    }

    public int MenuItemAt(double x, double y, in FrameState state, double scale)
    {
        LayoutMenu(in state);
        if (x < 0 || x >= _menuWidth || y < MenuPad)
        {
            return -1;
        }

        var top = MenuPad;
        for (var item = 0; item < _menuCount; item++)
        {
            var height = _menu[item] == MenuEntry.Separator ? MenuSeparatorHeight : _menuItemHeight;
            if (y < top + height)
            {
                return _menu[item] == MenuEntry.Separator ? -1 : item;
            }

            top += height;
        }

        return -1;
    }

    public FrameAction? MenuItemAction(int item, in FrameState state)
    {
        LayoutMenu(in state);
        if (item < 0 || item >= _menuCount)
        {
            return null;
        }

        return _menu[item] switch
        {
            MenuEntry.Minimize => new FrameAction(FrameActionKind.Minimize),
            MenuEntry.Maximize or MenuEntry.Unmaximize => new FrameAction(FrameActionKind.ToggleMaximize),
            MenuEntry.Shade or MenuEntry.Unshade => new FrameAction(FrameActionKind.ToggleShade),
            MenuEntry.Move => new FrameAction(FrameActionKind.Move),
            MenuEntry.Resize => new FrameAction(FrameActionKind.Resize),
            MenuEntry.Above => new FrameAction(FrameActionKind.ToggleAbove),
            MenuEntry.Stick => state.Sticky ? null : new FrameAction(FrameActionKind.ToggleSticky),
            MenuEntry.Unstick => state.Sticky ? new FrameAction(FrameActionKind.ToggleSticky) : null,
            MenuEntry.Close => new FrameAction(FrameActionKind.Close),
            _ => null,
        };
    }

    private void LayoutMenu(in FrameState state)
    {
        var caps = state.Capabilities;
        var n = 0;
        if (caps.HasFlag(FrameCapabilities.Minimize))
        {
            _menu[n++] = MenuEntry.Minimize;
        }

        if (caps.HasFlag(FrameCapabilities.Maximize))
        {
            _menu[n++] = state.Maximized ? MenuEntry.Unmaximize : MenuEntry.Maximize;
        }

        if (caps.HasFlag(FrameCapabilities.Shade))
        {
            _menu[n++] = state.Shaded ? MenuEntry.Unshade : MenuEntry.Shade;
        }

        _menu[n++] = MenuEntry.Move;
        _menu[n++] = MenuEntry.Resize;
        if (caps.HasFlag(FrameCapabilities.Above) || caps.HasFlag(FrameCapabilities.Stick))
        {
            _menu[n++] = MenuEntry.Separator;
            if (caps.HasFlag(FrameCapabilities.Above))
            {
                _menu[n++] = MenuEntry.Above;
            }

            if (caps.HasFlag(FrameCapabilities.Stick))
            {
                _menu[n++] = MenuEntry.Stick;
                _menu[n++] = MenuEntry.Unstick;
            }
        }

        _menu[n++] = MenuEntry.Separator;
        _menu[n++] = MenuEntry.Close;
        _menuCount = n;

        var font = _painter.Font.FontFor(1.0);
        var cache = _painter.Font.CacheFor(1.0);
        _menuItemHeight = _painter.Font.TextHeight(1.0) + 2 * MenuItemPad;
        var widest = 0f;
        var height = 2 * MenuPad;
        for (var i = 0; i < n; i++)
        {
            if (_menu[i] == MenuEntry.Separator)
            {
                height += MenuSeparatorHeight;
                continue;
            }

            height += _menuItemHeight;
            if (cache.TryGetBlob(Label(_menu[i]), font, out _, out var width))
            {
                widest = Math.Max(widest, width);
            }
        }

        _menuWidth = MenuTextInset + (int)Math.Ceiling(widest) + MenuTextRightPad;
        _menuHeight = height;
    }

    private void DrawMenuMark(SKCanvas canvas, MenuEntry entry, in FrameState state, int top, SKPaint fill)
    {
        var left = (MenuTextInset - MenuMarkSide) / 2f;
        var markTop = top + (_menuItemHeight - MenuMarkSide) / 2f;
        switch (entry)
        {
            case MenuEntry.Above:
            {
                var stroke = _painter.Resources.Stroke;
                stroke.Color = fill.Color;
                stroke.StrokeWidth = 1;
                stroke.PathEffect = null;
                canvas.DrawRect(left + 0.5f, markTop + 0.5f, MenuMarkSide - 1, MenuMarkSide - 1, stroke);
                if (state.Above)
                {
                    stroke.StrokeWidth = 2;
                    canvas.DrawLine(left + 2, markTop + 5, left + 4.5f, markTop + 7.5f, stroke);
                    canvas.DrawLine(left + 4.5f, markTop + 7.5f, left + 8.5f, markTop + 2.5f, stroke);
                    stroke.StrokeWidth = 1;
                }

                break;
            }

            case MenuEntry.Stick:
            case MenuEntry.Unstick:
            {
                var stroke = _painter.Resources.Stroke;
                stroke.Color = fill.Color;
                stroke.StrokeWidth = 1;
                stroke.PathEffect = null;
                var center = new SKPoint(left + MenuMarkSide / 2f, markTop + MenuMarkSide / 2f);
                canvas.DrawCircle(center, MenuMarkSide / 2f - 0.5f, stroke);
                if (state.Sticky == (entry == MenuEntry.Stick))
                {
                    canvas.DrawCircle(center, MenuMarkSide / 2f - 3f, fill);
                }

                break;
            }
        }
    }

    private static string Label(MenuEntry entry) => entry switch
    {
        MenuEntry.Minimize => "Minimize",
        MenuEntry.Maximize => "Maximize",
        MenuEntry.Unmaximize => "Unmaximize",
        MenuEntry.Shade => "Roll Up",
        MenuEntry.Unshade => "Unroll",
        MenuEntry.Move => "Move",
        MenuEntry.Resize => "Resize",
        MenuEntry.Above => "Always on Top",
        MenuEntry.Stick => "Always on Visible Workspace",
        MenuEntry.Unstick => "Only on This Workspace",
        MenuEntry.Close => "Close",
        _ => string.Empty,
    };

    private static SKColor Color(MetacityColor color) => new(color.ToArgb32());
}
