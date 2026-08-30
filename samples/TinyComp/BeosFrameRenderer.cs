using Basin;
using Basin.Capabilities;
using Basin.UI.Skia;
using SkiaSharp;

namespace TinyComp;

internal sealed class BeosFrameRenderer(FrameTheme theme) : IFrameRenderer
{
    private const int Border = 5;
    private const int TabHeight = 22;
    private const int TabPad = 5;
    private const int CloseSide = 12;
    private const int ZoomSide = 15;
    private const int WidgetGap = 6;
    private const int CornerZone = 16;

    private const int GripZone = 22;

    private static readonly SKColor Panel = new(0xD8, 0xD8, 0xD8);
    private static readonly SKColor PanelLight = new(0xFF, 0xFF, 0xFF);
    private static readonly SKColor PanelShade = new(0x9E, 0x9E, 0x9E);
    private static readonly SKColor FrameLine = new(0x3C, 0x3C, 0x3C);
    private static readonly SKColor TabActive = new(0xFF, 0xCB, 0x00);
    private static readonly SKColor TabActiveLight = new(0xFF, 0xE9, 0x6B);
    private static readonly SKColor TabActiveShade = new(0xB0, 0x8C, 0x00);
    private static readonly SKColor TabInactive = new(0xE8, 0xE8, 0xE8);
    private static readonly SKColor TabInactiveShade = new(0xA8, 0xA8, 0xA8);
    private static readonly SKColor TextActive = new(0x00, 0x00, 0x00);
    private static readonly SKColor TextInactive = new(0x68, 0x68, 0x68);
    private static readonly SKColor MenuHilite = new(0x33, 0x66, 0x99);
    private static readonly SKColor MenuHiliteText = new(0xFF, 0xFF, 0xFF);

    private int _outerWidth;
    private int _outerHeight;
    private int _tabWidth;
    private Box _close;
    private Box _zoom;

    public FrameInsets Measure(in FrameState state, double scale) =>
        new(TabHeight + Border, Border, Border, Border);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var skia = (ISkiaUISurface)surface;
        _outerWidth = clientBox.Width + 2 * Border;
        _outerHeight = clientBox.Height + TabHeight + 2 * Border;

        var title = string.IsNullOrEmpty(state.Title) ? state.AppId : state.Title;
        var titleWidth = 0f;
        if (!string.IsNullOrEmpty(title) && theme.TabText.TryGetBlob(title, theme.TabFont, out _, out var measured))
        {
            titleWidth = measured;
        }

        var hasZoom = state.Capabilities.HasFlag(FrameCapabilities.Maximize);
        var titleLeft = TabPad + CloseSide + WidgetGap;
        var tail = TabPad + (hasZoom ? ZoomSide + WidgetGap : 0);
        var minTab = titleLeft + tail;
        var wanted = titleLeft + (int)Math.Ceiling(titleWidth) + tail;
        _tabWidth = Math.Min(Math.Max(wanted, minTab), _outerWidth);

        _close = new Box(TabPad, (TabHeight - CloseSide) / 2, CloseSide, CloseSide);
        _zoom = hasZoom && _tabWidth >= minTab
            ? new Box(_tabWidth - TabPad - ZoomSide, (TabHeight - ZoomSide) / 2, ZoomSide, ZoomSide)
            : default;

        var canvas = skia.BeginDraw();
        try
        {
            DrawChrome(canvas, clientBox, state, interaction, title, titleLeft);
        }
        finally
        {
            skia.EndDraw();
        }
    }

    public FramePart PartAt(double x, double y, in FrameState state, double scale)
    {
        var w = _outerWidth;
        var h = _outerHeight;
        if (w <= 0 || x < 0 || y < 0 || x >= w || y >= h)
        {
            return FramePart.None;
        }

        if (y < TabHeight)
        {
            if (x >= _tabWidth)
            {
                return FramePart.None;
            }

            if (Hits(_close, x, y))
            {
                return FramePart.Close;
            }

            return Hits(_zoom, x, y) ? FramePart.Maximize : FramePart.Title;
        }

        var onTop = y < TabHeight + Border;
        var onBottom = y >= h - Border;
        var onLeft = x < Border;
        var onRight = x >= w - Border;
        if (!onTop && !onBottom && !onLeft && !onRight)
        {
            return FramePart.Border;
        }

        if (x >= w - GripZone && y >= h - GripZone)
        {
            return FramePart.BottomRight;
        }

        if (y < TabHeight + CornerZone)
        {
            if (x < CornerZone)
            {
                return FramePart.TopLeft;
            }

            if (x >= w - CornerZone)
            {
                return FramePart.TopRight;
            }
        }

        if (y >= h - CornerZone && x < CornerZone)
        {
            return FramePart.BottomLeft;
        }

        return onTop ? FramePart.Top
            : onBottom ? FramePart.Bottom
            : onLeft ? FramePart.Left
            : FramePart.Right;
    }

    public Box PartBounds(FramePart part) => part switch
    {
        FramePart.Close => _close,
        FramePart.Maximize => _zoom,
        _ => default,
    };

    public string? CursorFor(FramePart part) => part switch
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

    private const int MenuWidth = 150;
    private const int MenuItemHeight = 20;
    private const int MenuPadding = 3;

    private static int MenuItemCount(in FrameState state) =>
        1 + (state.Capabilities.HasFlag(FrameCapabilities.Minimize) ? 1 : 0)
          + (state.Capabilities.HasFlag(FrameCapabilities.Maximize) ? 1 : 0);

    public UISurfaceSize MeasureMenu(in FrameState state, double scale) =>
        new(MenuWidth, MenuItemCount(state) * MenuItemHeight + 2 * MenuPadding, scale);

    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
        var skia = (ISkiaUISurface)surface;
        var count = MenuItemCount(state);
        var height = count * MenuItemHeight + 2 * MenuPadding;
        var canvas = skia.BeginDraw();
        try
        {
            var fill = theme.Fill;
            fill.Color = Panel;
            canvas.DrawRect(0, 0, MenuWidth, height, fill);
            Outline(canvas, 0, 0, MenuWidth, height, FrameLine);
            Bevel(canvas, 1, 1, MenuWidth - 2, height - 2, PanelLight, PanelShade);

            for (var item = 0; item < count; item++)
            {
                var top = MenuPadding + item * MenuItemHeight;
                var text = TextActive;
                if (item == hotItem)
                {
                    fill.Color = MenuHilite;
                    canvas.DrawRect(MenuPadding, top, MenuWidth - 2 * MenuPadding, MenuItemHeight, fill);
                    text = MenuHiliteText;
                }

                var label = LabelOf(item, state);
                if (theme.TabText.TryGetBlob(label, theme.TabFont, out var blob, out _))
                {
                    fill.Color = text;
                    canvas.DrawText(blob, MenuPadding + 8, top + Baseline(MenuItemHeight), fill);
                }
            }
        }
        finally
        {
            skia.EndDraw();
        }
    }

    public int MenuItemAt(double x, double y, in FrameState state, double scale)
    {
        if (x < 0 || x >= MenuWidth || y < MenuPadding)
        {
            return -1;
        }

        var item = (int)((y - MenuPadding) / MenuItemHeight);
        return item < MenuItemCount(state) ? item : -1;
    }

    public FrameAction? MenuItemAction(int item, in FrameState state) => LabelOf(item, state) switch
    {
        "Close" => new FrameAction(FrameActionKind.Close),
        "Minimize" => new FrameAction(FrameActionKind.Minimize),
        _ => new FrameAction(FrameActionKind.ToggleMaximize),
    };

    private static string LabelOf(int item, in FrameState state)
    {
        if (state.Capabilities.HasFlag(FrameCapabilities.Minimize))
        {
            if (item == 0)
            {
                return "Minimize";
            }

            item--;
        }

        var hasZoom = state.Capabilities.HasFlag(FrameCapabilities.Maximize);
        return item == 0 && hasZoom ? (state.Maximized ? "Restore" : "Zoom") : "Close";
    }

    private static bool Hits(in Box box, double x, double y) =>
        !box.IsEmpty && x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;

    private float Baseline(int bandHeight)
    {
        var metrics = theme.TabFont.Metrics;
        return (bandHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;
    }

    private void DrawChrome(
        SKCanvas canvas,
        in Box clientBox,
        in FrameState state,
        in FrameInteraction interaction,
        string? title,
        int titleLeft)
    {
        var w = _outerWidth;
        var h = _outerHeight;
        var bodyTop = TabHeight;
        var fill = theme.Fill;

        canvas.Clear(SKColors.Transparent);

        var tab = state.Active ? TabActive : TabInactive;
        var tabLight = state.Active ? TabActiveLight : PanelLight;
        var tabShade = state.Active ? TabActiveShade : TabInactiveShade;
        fill.Color = tab;
        canvas.DrawRect(0, 0, _tabWidth, TabHeight, fill);
        fill.Color = tabLight;
        canvas.DrawRect(1, 1, _tabWidth - 2, 1, fill);
        canvas.DrawRect(1, 1, 1, TabHeight - 1, fill);
        fill.Color = tabShade;
        canvas.DrawRect(_tabWidth - 2, 1, 1, TabHeight - 1, fill);
        fill.Color = FrameLine;
        canvas.DrawRect(0, 0, _tabWidth, 1, fill);
        canvas.DrawRect(0, 0, 1, TabHeight, fill);
        canvas.DrawRect(_tabWidth - 1, 0, 1, TabHeight, fill);

        var bodyHeight = h - bodyTop;
        fill.Color = Panel;
        canvas.DrawRect(0, bodyTop, w, Border, fill);
        canvas.DrawRect(0, bodyTop + Border, Border, clientBox.Height, fill);
        canvas.DrawRect(w - Border, bodyTop + Border, Border, clientBox.Height, fill);
        canvas.DrawRect(0, h - Border, w, Border, fill);

        fill.Color = FrameLine;
        canvas.DrawRect(_tabWidth, bodyTop, w - _tabWidth, 1, fill);
        canvas.DrawRect(0, bodyTop, 1, bodyHeight, fill);
        canvas.DrawRect(w - 1, bodyTop, 1, bodyHeight, fill);
        canvas.DrawRect(0, h - 1, w, 1, fill);

        Bevel(canvas, 1, bodyTop + 1, w - 2, bodyHeight - 2, PanelLight, PanelShade);
        Bevel(canvas, Border - 1, bodyTop + Border - 1, clientBox.Width + 2, clientBox.Height + 2, PanelShade, PanelLight);

        DrawGrip(canvas, w, h);

        DrawCloseBox(canvas, interaction.Pressed == FramePart.Close);
        if (!_zoom.IsEmpty)
        {
            DrawZoomBox(canvas, interaction.Pressed == FramePart.Maximize);
        }

        var titleRight = _zoom.IsEmpty ? _tabWidth - TabPad : _zoom.X - WidgetGap;
        if (!string.IsNullOrEmpty(title) && titleRight - titleLeft > 8 &&
            theme.TabText.TryGetBlob(title, theme.TabFont, out var blob, out _))
        {
            fill.Color = state.Active ? TextActive : TextInactive;
            canvas.Save();
            canvas.ClipRect(new SKRect(titleLeft, 0, titleRight, TabHeight));
            canvas.DrawText(blob, titleLeft, Baseline(TabHeight), fill);
            canvas.Restore();
        }
    }

    private void DrawGrip(SKCanvas canvas, int w, int h)
    {
        var line = theme.Hairline;
        for (var i = 0; i < 3; i++)
        {
            var span = GripZone - 4 - i * 5;
            if (span <= 2)
            {
                break;
            }

            line.Color = FrameLine;
            canvas.DrawLine(w - span - 0.5f, h - 1.5f, w - 1.5f, h - span - 0.5f, line);
            line.Color = PanelLight;
            canvas.DrawLine(w - span - 1.5f, h - 1.5f, w - 1.5f, h - span - 1.5f, line);
        }
    }

    private void DrawCloseBox(SKCanvas canvas, bool pressed) => DrawWidgetFace(canvas, _close, pressed);

    private void DrawZoomBox(SKCanvas canvas, bool pressed)
    {
        DrawWidgetFace(canvas, _zoom, pressed);

        var line = theme.Hairline;
        line.Color = FrameLine;
        var push = pressed ? 1 : 0;
        canvas.DrawRect(_zoom.X + 4.5f + push, _zoom.Y + 4.5f + push, 8, 8, line);
        canvas.DrawRect(_zoom.X + 2.5f + push, _zoom.Y + 2.5f + push, 4, 4, line);
    }

    private void DrawWidgetFace(SKCanvas canvas, in Box box, bool pressed)
    {
        var fill = theme.Fill;
        fill.Color = Panel;
        canvas.DrawRect(box.X, box.Y, box.Width, box.Height, fill);
        Outline(canvas, box.X, box.Y, box.Width, box.Height, FrameLine);
        if (pressed)
        {
            Bevel(canvas, box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2, PanelShade, PanelLight);
        }
        else
        {
            Bevel(canvas, box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2, PanelLight, PanelShade);
        }
    }

    private void Outline(SKCanvas canvas, float x, float y, float width, float height, SKColor color)
    {
        var line = theme.Hairline;
        line.Color = color;
        canvas.DrawRect(x + 0.5f, y + 0.5f, width - 1, height - 1, line);
    }

    private void Bevel(SKCanvas canvas, float x, float y, float width, float height, SKColor light, SKColor shade)
    {
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var fill = theme.Fill;
        fill.Color = light;
        canvas.DrawRect(x, y, width - 1, 1, fill);
        canvas.DrawRect(x, y, 1, height - 1, fill);
        fill.Color = shade;
        canvas.DrawRect(x, y + height - 1, width, 1, fill);
        canvas.DrawRect(x + width - 1, y, 1, height, fill);
    }
}
