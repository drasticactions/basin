using System.Reflection;
using Basin;
using Basin.Render.Skia;
using Basin.Capabilities;
using Basin.UI.Skia;
using SkiaSharp;

namespace TinyComp;

internal sealed class SkiaFrameRenderer(FrameTheme theme) : IFrameRenderer
{
    private const int Border = 4;
    private const int TitleHeight = 26;
    private const int CornerZone = 16;
    private const int ButtonWidth = 32;
    private const int ButtonGap = 2;

    private static readonly SKColor ChromeActive = new(0x23, 0x25, 0x2B);
    private static readonly SKColor ChromeInactive = new(0x17, 0x18, 0x1C);
    private static readonly SKColor Outline = new(0x0A, 0x0B, 0x0D);
    private static readonly SKColor TextActive = new(0xDE, 0xE1, 0xE6);
    private static readonly SKColor TextInactive = new(0x80, 0x84, 0x8C);
    private static readonly SKColor CloseHot = new(0xC2, 0x40, 0x40);
    private static readonly SKColor ClosePressed = new(0x95, 0x2F, 0x2F);
    private static readonly SKColor ButtonHot = new(0x3A, 0x3E, 0x47);
    private static readonly SKColor ButtonPressed = new(0x2C, 0x2F, 0x36);
    private static readonly SKColor[] BadgePalette =
    [
        new(0x5B, 0x7B, 0xA8), new(0x6E, 0x9E, 0x6A), new(0xA8, 0x7B, 0x5B),
        new(0x9A, 0x6A, 0x9E), new(0x5B, 0xA8, 0x99), new(0xA8, 0x5B, 0x6E),
        new(0x8C, 0x8C, 0x5B), new(0x6A, 0x74, 0x9E),
    ];

    private int _outerWidth;
    private int _outerHeight;
    private Box _close;
    private Box _maximize;
    private Box _icon;

    public FrameInsets Measure(in FrameState state, double scale) =>
        new(Border + TitleHeight, Border, Border, Border);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var skia = (ISkiaUISurface)surface;
        _outerWidth = clientBox.Width + 2 * Border;
        _outerHeight = clientBox.Height + TitleHeight + 2 * Border;

        var barTop = Border;
        var buttonY = barTop + (TitleHeight - 20) / 2;
        var x = _outerWidth - Border - 4 - ButtonWidth;
        var buttonsFit = clientBox.Width > 4 * (ButtonWidth + ButtonGap);
        _close = buttonsFit ? new Box(x, buttonY, ButtonWidth, 20) : default;
        var showMaximize = buttonsFit && state.Capabilities.HasFlag(FrameCapabilities.Maximize);
        x -= showMaximize ? ButtonWidth + ButtonGap : 0;
        _maximize = showMaximize ? new Box(x, buttonY, ButtonWidth, 20) : default;
        var iconSide = 18;
        _icon = new Box(Border + 6, barTop + (TitleHeight - iconSide) / 2, iconSide, iconSide);

        var canvas = skia.BeginDraw();
        try
        {
            DrawChrome(canvas, clientBox, state, interaction);
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

        const int b = Border;
        var onEdge = x < b || x >= w - b || y < b || y >= h - b;
        if (onEdge)
        {
            var nearLeft = x < CornerZone;
            var nearRight = x >= w - CornerZone;
            if (y < CornerZone && (x < b || y < b))
            {
                if (nearLeft)
                {
                    return FramePart.TopLeft;
                }

                if (nearRight)
                {
                    return FramePart.TopRight;
                }
            }

            if (y >= h - CornerZone)
            {
                if (nearLeft)
                {
                    return FramePart.BottomLeft;
                }

                if (nearRight)
                {
                    return FramePart.BottomRight;
                }
            }

            return y < b ? FramePart.Top
                : y >= h - b ? FramePart.Bottom
                : x < b ? FramePart.Left
                : FramePart.Right;
        }

        if (y < b + TitleHeight)
        {
            if (Hits(_close, x, y))
            {
                return FramePart.Close;
            }

            if (Hits(_maximize, x, y))
            {
                return FramePart.Maximize;
            }

            if (Hits(_icon, x, y))
            {
                return FramePart.Icon;
            }

            return FramePart.Title;
        }

        return FramePart.Border;
    }

    public Box PartBounds(FramePart part) => part switch
    {
        FramePart.Close => _close,
        FramePart.Maximize => _maximize,
        _ => default,
    };

    private const int MenuWidth = 180;
    private const int MenuItemHeight = 30;
    private const int MenuPadding = 4;

    private static int MenuItemCount(in FrameState state) =>
        1 + (state.Capabilities.HasFlag(FrameCapabilities.Maximize) ? 1 : 0);

    public UISurfaceSize MeasureMenu(in FrameState state, double scale) =>
        new(MenuWidth, MenuItemCount(state) * MenuItemHeight + 2 * MenuPadding, scale);

    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
        var skia = (ISkiaUISurface)surface;
        var count = MenuItemCount(state);
        var canvas = skia.BeginDraw();
        try
        {
            var fill = theme.Fill;
            fill.Color = new SKColor(0x1F, 0x21, 0x27);
            canvas.DrawRect(0, 0, MenuWidth, count * MenuItemHeight + 2 * MenuPadding, fill);
            var stroke = theme.Stroke;
            stroke.Color = Outline;
            stroke.StrokeWidth = 1;
            canvas.DrawRect(0.5f, 0.5f, MenuWidth - 1, count * MenuItemHeight + 2 * MenuPadding - 1, stroke);

            for (var item = 0; item < count; item++)
            {
                var top = MenuPadding + item * MenuItemHeight;
                if (item == hotItem)
                {
                    fill.Color = ButtonHot;
                    canvas.DrawRoundRect(MenuPadding, top + 1, MenuWidth - 2 * MenuPadding, MenuItemHeight - 2, 4, 4, fill);
                }

                var label = LabelOf(item, state);
                if (theme.Text.TryGetBlob(label, theme.TitleFont, out var blob, out _))
                {
                    var metrics = theme.TitleFont.Metrics;
                    fill.Color = TextActive;
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
        "Close" => new FrameAction(FrameActionKind.Close),
        _ => new FrameAction(FrameActionKind.ToggleMaximize),
    };

    private static string LabelOf(int item, in FrameState state)
    {
        var hasMaximize = state.Capabilities.HasFlag(FrameCapabilities.Maximize);
        return item == 0 && hasMaximize ? (state.Maximized ? "Restore" : "Maximize") : "Close";
    }

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

    private static bool Hits(in Box box, double x, double y) =>
        !box.IsEmpty && x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;

    private void DrawChrome(SKCanvas canvas, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var chrome = state.Active ? ChromeActive : ChromeInactive;
        var text = state.Active ? TextActive : TextInactive;
        var fill = theme.Fill;
        var w = _outerWidth;
        var h = _outerHeight;

        fill.Color = chrome;
        var barBottom = Border + TitleHeight;
        canvas.DrawRect(0, 0, w, barBottom, fill);
        canvas.DrawRect(0, barBottom, Border, clientBox.Height, fill);
        canvas.DrawRect(w - Border, barBottom, Border, clientBox.Height, fill);
        canvas.DrawRect(0, h - Border, w, Border, fill);

        var stroke = theme.Stroke;
        stroke.Color = Outline;
        stroke.StrokeWidth = 1;
        canvas.DrawRect(0.5f, 0.5f, w - 1, h - 1, stroke);

        DrawIcon(canvas, state, text);

        var titleRight = w - Border - 8;
        if (!_close.IsEmpty)
        {
            DrawButtonBack(canvas, _close, interaction, FramePart.Close, CloseHot, ClosePressed);
            stroke.Color = interaction.Hot == FramePart.Close ? TextActive : text;
            stroke.StrokeWidth = 1.4f;
            var c = Center(_close);
            canvas.DrawLine(c.X - 4, c.Y - 4, c.X + 4, c.Y + 4, stroke);
            canvas.DrawLine(c.X - 4, c.Y + 4, c.X + 4, c.Y - 4, stroke);
            titleRight = _close.X - 8;
        }

        if (!_maximize.IsEmpty)
        {
            DrawButtonBack(canvas, _maximize, interaction, FramePart.Maximize, ButtonHot, ButtonPressed);
            stroke.Color = interaction.Hot == FramePart.Maximize ? TextActive : text;
            stroke.StrokeWidth = 1.2f;
            var c = Center(_maximize);
            if (state.Maximized)
            {
                canvas.DrawRect(c.X - 4.5f, c.Y - 2.5f, 7, 7, stroke);
                canvas.DrawLine(c.X - 1.5f, c.Y - 4.5f, c.X + 4.5f, c.Y - 4.5f, stroke);
                canvas.DrawLine(c.X + 4.5f, c.Y - 4.5f, c.X + 4.5f, c.Y + 1.5f, stroke);
            }
            else
            {
                canvas.DrawRect(c.X - 3.5f, c.Y - 3.5f, 7, 7, stroke);
            }

            titleRight = _maximize.X - 8;
        }

        var title = string.IsNullOrEmpty(state.Title) ? state.AppId : state.Title;
        var titleLeft = _icon.Right + 8;
        if (!string.IsNullOrEmpty(title) && titleRight - titleLeft > 24 &&
            theme.Text.TryGetBlob(title, theme.TitleFont, out var blob, out _))
        {
            var metrics = theme.TitleFont.Metrics;
            var baseline = Border + (TitleHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;
            fill.Color = text;
            canvas.Save();
            canvas.ClipRect(new SKRect(titleLeft, 0, titleRight, barBottom));
            canvas.DrawText(blob, titleLeft, baseline, fill);
            canvas.Restore();
        }
    }

    private void DrawButtonBack(SKCanvas canvas, in Box box, in FrameInteraction interaction, FramePart part, SKColor hot, SKColor pressed)
    {
        if (interaction.Pressed == part && interaction.Hot == part)
        {
            theme.Fill.Color = pressed;
        }
        else if (interaction.Hot == part && interaction.Pressed is FramePart.None || interaction.Pressed == part)
        {
            theme.Fill.Color = hot;
        }
        else
        {
            return;
        }

        canvas.DrawRoundRect(box.X, box.Y, box.Width, box.Height, 4, 4, theme.Fill);
    }

    private void DrawIcon(SKCanvas canvas, in FrameState state, SKColor text)
    {
        var box = new SKRect(_icon.X, _icon.Y, _icon.Right, _icon.Bottom);
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

        var label = string.IsNullOrEmpty(state.AppId) ? state.Title : state.AppId;
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        var sum = 0;
        foreach (var ch in label)
        {
            sum = (sum + ch) % BadgePalette.Length;
        }

        theme.Fill.Color = state.Active ? BadgePalette[sum] : BadgePalette[sum].WithAlpha(0x90);
        canvas.DrawRoundRect(box.Left, box.Top, box.Width, box.Height, 4, 4, theme.Fill);
        var initial = label[..1].ToUpperInvariant();
        if (theme.Badges.TryGetBlob(initial, theme.BadgeFont, out var blob, out var width))
        {
            var metrics = theme.BadgeFont.Metrics;
            theme.Fill.Color = new SKColor(0xF2, 0xF3, 0xF5);
            canvas.DrawText(
                blob,
                box.Left + (box.Width - width) / 2,
                box.Top + (box.Height - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent,
                theme.Fill);
        }
    }

    private static (float X, float Y) Center(in Box box) =>
        (box.X + box.Width / 2f, box.Y + box.Height / 2f);
}
