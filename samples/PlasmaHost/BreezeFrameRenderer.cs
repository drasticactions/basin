using Basin;
using Basin.Capabilities;
using Basin.UI.Skia;
using SkiaSharp;

namespace PlasmaHost;

internal sealed class BreezeFrameRenderer(BreezeTheme theme) : IFrameRenderer
{
    private const int TitleHeight = 30;
    private const int ButtonHit = 24;
    private const int ButtonCircle = 20;
    private const int ButtonGap = 2;
    private const int EdgePad = 4;
    private const int ResizeBand = 4;
    private const int CornerZone = 24;
    internal const int CornerRadius = 5;

    private int _outerWidth;
    private int _outerHeight;
    private int _border;
    private Box _menu;
    private Box _minimize;
    private Box _maximize;
    private Box _close;
    private int _titleLeft;
    private int _titleRight;

    public FrameInsets Measure(in FrameState state, double scale)
    {
        var border = theme.Config.BorderWidth;
        return new FrameInsets(TitleHeight, border, border, border);
    }

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var skia = (ISkiaUISurface)surface;
        Layout(clientBox, state);

        var palette = theme.PaletteFor(state.Palette);
        var bg = state.Active ? palette.ActiveBackground : palette.InactiveBackground;
        var fg = state.Active ? palette.ActiveForeground : palette.InactiveForeground;

        var canvas = skia.BeginDraw();
        try
        {
            canvas.Clear(SKColors.Transparent);
            var fill = theme.Fill;
            fill.Color = bg;
            var radius = state.Maximized ? 0 : CornerRadius;
            canvas.DrawRoundRect(0, 0, _outerWidth, TitleHeight, radius, radius, fill);
            if (radius > 0)
            {
                canvas.DrawRect(0, TitleHeight - radius, _outerWidth, radius, fill);
            }

            if (_border > 0)
            {
                canvas.DrawRect(0, TitleHeight, _border, clientBox.Height, fill);
                canvas.DrawRect(_outerWidth - _border, TitleHeight, _border, clientBox.Height, fill);
                canvas.DrawRect(0, _outerHeight - _border, _outerWidth, _border, fill);
            }

            DrawMenuButton(canvas, state, fg, bg, palette, interaction);
            DrawGlyphButton(canvas, _minimize, FramePart.Minimize, fg, bg, palette, interaction, state);
            DrawGlyphButton(canvas, _maximize, FramePart.Maximize, fg, bg, palette, interaction, state);
            DrawGlyphButton(canvas, _close, FramePart.Close, fg, bg, palette, interaction, state);

            var title = string.IsNullOrEmpty(state.Title) ? state.AppId : state.Title;
            if (!string.IsNullOrEmpty(title) && _titleRight - _titleLeft > 24 &&
                theme.Text.TryGetBlob(title, theme.TitleFont, out var blob, out var width))
            {
                var metrics = theme.TitleFont.Metrics;
                var baseline = (TitleHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;
                var x = (_outerWidth - width) / 2;
                x = Math.Clamp(x, _titleLeft, Math.Max(_titleLeft, _titleRight - (int)width));
                fill.Color = fg;
                canvas.Save();
                canvas.ClipRect(new SKRect(_titleLeft, 0, _titleRight, TitleHeight));
                canvas.DrawText(blob, x, baseline, fill);
                canvas.Restore();
            }
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

        if (!state.Maximized && !state.Fullscreen)
        {
            if (y < ResizeBand)
            {
                return x < CornerZone ? FramePart.TopLeft
                    : x >= w - CornerZone ? FramePart.TopRight
                    : FramePart.Top;
            }

            if (x < ResizeBand && y < CornerZone)
            {
                return FramePart.TopLeft;
            }

            if (x >= w - ResizeBand && y < CornerZone)
            {
                return FramePart.TopRight;
            }
        }

        if (y < TitleHeight)
        {
            if (Hits(_close, x, y))
            {
                return FramePart.Close;
            }

            if (Hits(_maximize, x, y))
            {
                return FramePart.Maximize;
            }

            if (Hits(_minimize, x, y))
            {
                return FramePart.Minimize;
            }

            if (Hits(_menu, x, y))
            {
                return FramePart.Menu;
            }

            return FramePart.Title;
        }

        if (_border > 0)
        {
            var nearBottom = y >= h - CornerZone;
            if (x < _border)
            {
                return nearBottom ? FramePart.BottomLeft : FramePart.Left;
            }

            if (x >= w - _border)
            {
                return nearBottom ? FramePart.BottomRight : FramePart.Right;
            }

            if (y >= h - _border)
            {
                return x < CornerZone ? FramePart.BottomLeft
                    : x >= w - CornerZone ? FramePart.BottomRight
                    : FramePart.Bottom;
            }
        }

        return FramePart.Border;
    }

    public Box PartBounds(FramePart part) => part switch
    {
        FramePart.Close => _close,
        FramePart.Maximize => _maximize,
        FramePart.Minimize => _minimize,
        FramePart.Menu => _menu,
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

    private const int MenuWidth = 180;
    private const int MenuItemHeight = 30;
    private const int MenuPadding = 4;

    private static int MenuItemCount(in FrameState state) =>
        1 + (state.Capabilities.HasFlag(FrameCapabilities.Minimize) ? 1 : 0)
          + (state.Capabilities.HasFlag(FrameCapabilities.Maximize) ? 1 : 0);

    public UISurfaceSize MeasureMenu(in FrameState state, double scale) =>
        new(MenuWidth, MenuItemCount(state) * MenuItemHeight + 2 * MenuPadding, scale);

    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
        var skia = (ISkiaUISurface)surface;
        var palette = theme.PaletteFor(state.Palette);
        var count = MenuItemCount(state);
        var height = count * MenuItemHeight + 2 * MenuPadding;
        var canvas = skia.BeginDraw();
        try
        {
            var fill = theme.Fill;
            fill.Color = palette.ActiveBackground;
            canvas.DrawRect(0, 0, MenuWidth, height, fill);
            var stroke = theme.Stroke;
            stroke.Color = palette.ActiveForeground.WithAlpha(0x50);
            stroke.StrokeWidth = 1;
            canvas.DrawRect(0.5f, 0.5f, MenuWidth - 1, height - 1, stroke);

            for (var item = 0; item < count; item++)
            {
                var top = MenuPadding + item * MenuItemHeight;
                var text = palette.ActiveForeground;
                if (item == hotItem)
                {
                    fill.Color = palette.Focus;
                    canvas.DrawRoundRect(MenuPadding, top + 1, MenuWidth - 2 * MenuPadding, MenuItemHeight - 2, 4, 4, fill);
                    text = palette.ActiveBackground;
                }

                var label = LabelOf(item, state);
                if (theme.Text.TryGetBlob(label, theme.TitleFont, out var blob, out _))
                {
                    var metrics = theme.TitleFont.Metrics;
                    fill.Color = text;
                    canvas.DrawText(blob, MenuPadding + 10, top + (MenuItemHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent, fill);
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
        "Minimize" => new FrameAction(FrameActionKind.Minimize),
        "Close" => new FrameAction(FrameActionKind.Close),
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

        if (state.Capabilities.HasFlag(FrameCapabilities.Maximize))
        {
            if (item == 0)
            {
                return state.Maximized ? "Restore" : "Maximize";
            }
        }

        return "Close";
    }

    private void Layout(in Box clientBox, in FrameState state)
    {
        _border = theme.Config.BorderWidth;
        _outerWidth = clientBox.Width + 2 * _border;
        _outerHeight = clientBox.Height + TitleHeight + _border;
        _menu = default;
        _minimize = default;
        _maximize = default;
        _close = default;

        var y = (TitleHeight - ButtonHit) / 2;
        var left = EdgePad;
        foreach (var part in theme.Config.LeftButtons)
        {
            if (!Wanted(part, state) || left + ButtonHit > _outerWidth / 2)
            {
                continue;
            }

            Assign(part, new Box(left, y, ButtonHit, ButtonHit));
            left += ButtonHit + ButtonGap;
        }

        var right = _outerWidth - EdgePad;
        for (var i = theme.Config.RightButtons.Count - 1; i >= 0; i--)
        {
            var part = theme.Config.RightButtons[i];
            if (!Wanted(part, state) || right - ButtonHit < _outerWidth / 2)
            {
                continue;
            }

            right -= ButtonHit;
            Assign(part, new Box(right, y, ButtonHit, ButtonHit));
            right -= ButtonGap;
        }

        _titleLeft = left + EdgePad;
        _titleRight = right + ButtonGap - EdgePad;
    }

    private static bool Wanted(FramePart part, in FrameState state) => part switch
    {
        FramePart.Minimize => state.Capabilities.HasFlag(FrameCapabilities.Minimize),
        FramePart.Maximize => state.Capabilities.HasFlag(FrameCapabilities.Maximize),
        _ => true,
    };

    private void Assign(FramePart part, in Box box)
    {
        switch (part)
        {
            case FramePart.Menu:
                _menu = box;
                break;
            case FramePart.Minimize:
                _minimize = box;
                break;
            case FramePart.Maximize:
                _maximize = box;
                break;
            case FramePart.Close:
                _close = box;
                break;
        }
    }

    private void DrawMenuButton(
        SKCanvas canvas, in FrameState state, SKColor fg, SKColor bg, in BreezePalette palette, in FrameInteraction interaction)
    {
        if (_menu.IsEmpty)
        {
            return;
        }

        var glyph = DrawButtonBack(canvas, _menu, FramePart.Menu, fg, bg, palette, interaction, negative: false);
        var box = new SKRect(_menu.X + 3, _menu.Y + 3, _menu.Right - 3, _menu.Bottom - 3);
        if (state.Icon.Pixels is { } pixels && theme.ImageFor(pixels) is { } clientIcon)
        {
            canvas.DrawImage(clientIcon, box, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return;
        }

        if (state.Icon.Name is { Length: > 0 } name && theme.IconFor(name) is { } image)
        {
            canvas.DrawImage(image, box, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return;
        }

        var appId = string.IsNullOrEmpty(state.AppId) ? state.Title : state.AppId;
        if (appId is { Length: > 0 } && theme.IconFor(appId) is { } fallback)
        {
            canvas.DrawImage(fallback, box, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return;
        }

        var stroke = theme.Stroke;
        stroke.Color = glyph;
        stroke.StrokeWidth = 1.1f;
        var (cx, cy) = Center(_menu);
        for (var i = -1; i <= 1; i++)
        {
            canvas.DrawLine(cx - 4.5f, cy + i * 3.5f, cx + 4.5f, cy + i * 3.5f, stroke);
        }
    }

    private void DrawGlyphButton(
        SKCanvas canvas, in Box box, FramePart part, SKColor fg, SKColor bg, in BreezePalette palette,
        in FrameInteraction interaction, in FrameState state)
    {
        if (box.IsEmpty)
        {
            return;
        }

        var glyph = DrawButtonBack(canvas, box, part, fg, bg, palette, interaction, negative: part == FramePart.Close);
        var stroke = theme.Stroke;
        stroke.Color = glyph;
        stroke.StrokeWidth = 1.1f;
        var (cx, cy) = Center(box);
        switch (part)
        {
            case FramePart.Close:
                canvas.DrawLine(cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f, stroke);
                canvas.DrawLine(cx - 4.5f, cy + 4.5f, cx + 4.5f, cy - 4.5f, stroke);
                break;
            case FramePart.Maximize when state.Maximized:
                canvas.DrawLine(cx - 4.5f, cy, cx, cy - 4.5f, stroke);
                canvas.DrawLine(cx, cy - 4.5f, cx + 4.5f, cy, stroke);
                canvas.DrawLine(cx - 4.5f, cy, cx, cy + 4.5f, stroke);
                canvas.DrawLine(cx, cy + 4.5f, cx + 4.5f, cy, stroke);
                break;
            case FramePart.Maximize:
                canvas.DrawLine(cx - 4.5f, cy + 2.25f, cx, cy - 2.25f, stroke);
                canvas.DrawLine(cx, cy - 2.25f, cx + 4.5f, cy + 2.25f, stroke);
                break;
            case FramePart.Minimize:
                canvas.DrawLine(cx - 4.5f, cy - 2.25f, cx, cy + 2.25f, stroke);
                canvas.DrawLine(cx, cy + 2.25f, cx + 4.5f, cy - 2.25f, stroke);
                break;
        }
    }

    private SKColor DrawButtonBack(
        SKCanvas canvas, in Box box, FramePart part, SKColor fg, SKColor bg, in BreezePalette palette,
        in FrameInteraction interaction, bool negative)
    {
        var accent = negative ? palette.Negative : palette.Focus;
        SKColor back;
        if (interaction.Pressed == part && interaction.Hot == part)
        {
            back = Darken(accent);
        }
        else if ((interaction.Hot == part && interaction.Pressed is FramePart.None) || interaction.Pressed == part)
        {
            back = accent;
        }
        else
        {
            return fg;
        }

        theme.Fill.Color = back;
        var (cx, cy) = Center(box);
        canvas.DrawCircle(cx, cy, ButtonCircle / 2f, theme.Fill);
        return bg;
    }

    private static SKColor Darken(SKColor color) => new(
        (byte)(color.Red * 8 / 10), (byte)(color.Green * 8 / 10), (byte)(color.Blue * 8 / 10));

    private static bool Hits(in Box box, double x, double y) =>
        !box.IsEmpty && x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;

    private static (float X, float Y) Center(in Box box) =>
        (box.X + box.Width / 2f, box.Y + box.Height / 2f);
}
