using Basin;
using Basin.Render.Skia;
using Basin.Capabilities;
using SkiaSharp;

namespace Basin.UI.Skia;

/// <summary>
/// A flat, opaque frame look with a title bar, close, maximize and minimize buttons, and a window menu. It keeps the geometry of its last draw, so each window needs its own instance.
/// </summary>
/// <param name="theme">The fonts, paints and icons to draw with. The renderer does not own it.</param>
public sealed class FlatFrameRenderer(FlatFrameTheme theme) : IFrameRenderer
{
    /// <summary>
    /// Gets <see cref="ISkiaUISurface"/>, because the renderer draws with a Skia canvas.
    /// </summary>
    public Type SurfaceContract => typeof(ISkiaUISurface);

    /// <summary>
    /// Gets a value indicating whether every pixel of the frame is opaque. It is always <see langword="true"/>.
    /// </summary>
    public bool OpaqueChrome => true;

    private const int Border = 4;
    private const int TitleHeight = 26;
    private const int CornerZone = 16;
    private const int ButtonWidth = 32;
    private const int ButtonGap = 2;

    private Palette _palette = Palette.Dark;

    private readonly record struct Palette(
        SKColor ChromeActive, SKColor ChromeInactive, SKColor Outline, SKColor TextActive, SKColor TextInactive,
        SKColor CloseHot, SKColor ClosePressed, SKColor ButtonHot, SKColor ButtonPressed, SKColor Menu)
    {
        public static readonly Palette Dark = new(
            new(0x23, 0x25, 0x2B), new(0x17, 0x18, 0x1C), new(0x0A, 0x0B, 0x0D), new(0xDE, 0xE1, 0xE6), new(0x80, 0x84, 0x8C),
            new(0xC2, 0x40, 0x40), new(0x95, 0x2F, 0x2F), new(0x3A, 0x3E, 0x47), new(0x2C, 0x2F, 0x36), new(0x1F, 0x21, 0x27));

        public static readonly Palette Light = new(
            new(0xEC, 0xED, 0xF0), new(0xF6, 0xF6, 0xF8), new(0xB8, 0xBB, 0xC2), new(0x22, 0x24, 0x29), new(0x80, 0x84, 0x8C),
            new(0xD9, 0x4A, 0x4A), new(0xB0, 0x38, 0x38), new(0xD6, 0xD8, 0xDD), new(0xC4, 0xC7, 0xCE), new(0xF4, 0xF5, 0xF7));

        public static readonly Palette HighContrast = new(
            new(0x00, 0x00, 0x00), new(0x00, 0x00, 0x00), new(0xFF, 0xFF, 0x00), new(0xFF, 0xFF, 0xFF), new(0xFF, 0xFF, 0x00),
            new(0xC0, 0x00, 0x00), new(0x80, 0x00, 0x00), new(0x40, 0x40, 0x00), new(0x60, 0x60, 0x00), new(0x00, 0x00, 0x00));

        public static Palette For(FrameColorScheme scheme, bool highContrast) =>
            highContrast ? HighContrast : scheme == FrameColorScheme.Light ? Light : Dark;
    }

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
    private Box _minimize;
    private Box _icon;

    /// <summary>
    /// Measures the frame: a 4-pixel border on each side, and a 26-pixel title bar on top. The state and the scale do not change it.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The insets in logical pixels.</returns>
    public FrameInsets Measure(in FrameState state, double scale) =>
        new(Border + TitleHeight, Border, Border, Border);

    /// <summary>
    /// Draws the frame and stores the button boxes for <see cref="PartAt"/> and <see cref="PartBounds"/>. The buttons are left out when the client is too narrow for them.
    /// </summary>
    /// <param name="surface">The surface to draw into. It must be an <see cref="ISkiaUISurface"/>.</param>
    /// <param name="clientBox">The client area in surface coordinates.</param>
    /// <param name="state">The window state.</param>
    /// <param name="interaction">The pointer state and the change times.</param>
    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var skia = (ISkiaUISurface)surface;
        _outerWidth = clientBox.Width + 2 * Border;
        _outerHeight = clientBox.Height + TitleHeight + 2 * Border;

        var barTop = Border;
        var buttonY = barTop + (TitleHeight - 20) / 2;
        var x = _outerWidth - Border - 4 - ButtonWidth;
        var hasMaximize = state.Capabilities.HasFlag(FrameCapabilities.Maximize);
        var hasMinimize = state.Capabilities.HasFlag(FrameCapabilities.Minimize);
        var slots = 1 + (hasMaximize ? 1 : 0) + (hasMinimize ? 1 : 0);
        var buttonsFit = clientBox.Width > (slots + 2) * (ButtonWidth + ButtonGap);
        _close = buttonsFit ? new Box(x, buttonY, ButtonWidth, 20) : default;
        var showMaximize = buttonsFit && hasMaximize;
        x -= showMaximize ? ButtonWidth + ButtonGap : 0;
        _maximize = showMaximize ? new Box(x, buttonY, ButtonWidth, 20) : default;
        var showMinimize = buttonsFit && hasMinimize;
        x -= showMinimize ? ButtonWidth + ButtonGap : 0;
        _minimize = showMinimize ? new Box(x, buttonY, ButtonWidth, 20) : default;
        var iconSide = 18;
        _icon = new Box(Border + 6, barTop + (TitleHeight - iconSide) / 2, iconSide, iconSide);

        var canvas = skia.BeginDraw();
        try
        {
            DrawChrome(canvas, clientBox, state, interaction, (int)Math.Ceiling(surface.Size.Scale));
        }
        finally
        {
            skia.EndDraw();
        }
    }

    /// <summary>
    /// Finds the frame part at a point of the last draw. The corners take 16 pixels along each edge.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The part, or <see cref="FramePart.None"/> outside the frame or before the first draw.</returns>
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

            if (Hits(_minimize, x, y))
            {
                return FramePart.Minimize;
            }

            if (Hits(_icon, x, y))
            {
                return FramePart.Icon;
            }

            return FramePart.Title;
        }

        return FramePart.Border;
    }

    /// <summary>
    /// Gives the box of a button in the last draw.
    /// </summary>
    /// <param name="part">The part to look up.</param>
    /// <returns>The box for the close, maximize or minimize button. An empty box for other parts and for a hidden button.</returns>
    public Box PartBounds(FramePart part) => part switch
    {
        FramePart.Close => _close,
        FramePart.Maximize => _maximize,
        FramePart.Minimize => _minimize,
        _ => default,
    };

    private const int MenuWidth = 180;
    private const int MenuItemHeight = 30;
    private const int MenuPadding = 4;

    private static int MenuItemCount(in FrameState state) =>
        1 + (state.Capabilities.HasFlag(FrameCapabilities.Minimize) ? 1 : 0)
          + (state.Capabilities.HasFlag(FrameCapabilities.Maximize) ? 1 : 0)
          + (state.Capabilities.HasFlag(FrameCapabilities.X11Density) ? 1 : 0);

    /// <summary>
    /// Measures the window menu: 180 pixels wide, with one 30-pixel row for each item.
    /// </summary>
    /// <param name="state">The window state. Its capabilities choose the items.</param>
    /// <param name="scale">The output scale.</param>
    /// <returns>The menu size in logical pixels.</returns>
    public UISurfaceSize MeasureMenu(in FrameState state, double scale) =>
        new(MenuWidth, MenuItemCount(state) * MenuItemHeight + 2 * MenuPadding, scale);

    /// <summary>
    /// Draws the window menu. The items are Minimize, Maximize or Restore, the X11 density toggle and Close, each only when the window has the capability.
    /// </summary>
    /// <param name="surface">The menu surface. It must be an <see cref="ISkiaUISurface"/>.</param>
    /// <param name="state">The window state.</param>
    /// <param name="hotItem">The item under the pointer, or -1 for none.</param>
    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
        var skia = (ISkiaUISurface)surface;
        var count = MenuItemCount(state);
        _palette = Palette.For(state.ColorScheme, theme.HighContrast);
        var canvas = skia.BeginDraw();
        try
        {
            var fill = theme.Fill;
            fill.Color = _palette.Menu;
            canvas.DrawRect(0, 0, MenuWidth, count * MenuItemHeight + 2 * MenuPadding, fill);
            var stroke = theme.Stroke;
            stroke.Color = _palette.Outline;
            stroke.StrokeWidth = 1;
            canvas.DrawRect(0.5f, 0.5f, MenuWidth - 1, count * MenuItemHeight + 2 * MenuPadding - 1, stroke);

            for (var item = 0; item < count; item++)
            {
                var top = MenuPadding + item * MenuItemHeight;
                if (item == hotItem)
                {
                    fill.Color = _palette.ButtonHot;
                    canvas.DrawRoundRect(MenuPadding, top + 1, MenuWidth - 2 * MenuPadding, MenuItemHeight - 2, 4, 4, fill);
                }

                var label = LabelOf(item, state);
                if (theme.Text.TryGetBlob(label, theme.TitleFont, out var blob, out _))
                {
                    var metrics = theme.TitleFont.Metrics;
                    fill.Color = _palette.TextActive;
                    canvas.DrawText(blob, MenuPadding + 10, top + (MenuItemHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent, fill);
                }
            }
        }
        finally
        {
            skia.EndDraw();
        }
    }

    /// <inheritdoc/>
    public int MenuItemAt(double x, double y, in FrameState state, double scale)
    {
        if (x < 0 || x >= MenuWidth || y < MenuPadding)
        {
            return -1;
        }

        var item = (int)((y - MenuPadding) / MenuItemHeight);
        return item < MenuItemCount(state) ? item : -1;
    }

    /// <summary>
    /// Gives the action of a menu item. An index past the last item gives the close action.
    /// </summary>
    /// <param name="item">The item index that <see cref="MenuItemAt"/> returned.</param>
    /// <param name="state">The window state.</param>
    /// <returns>The action. It is never <see langword="null"/>.</returns>
    public FrameAction? MenuItemAction(int item, in FrameState state) => LabelOf(item, state) switch
    {
        "Close" => new FrameAction(FrameActionKind.Close),
        "Minimize" => new FrameAction(FrameActionKind.Minimize),
        DensityLow or DensityFull => new FrameAction(FrameActionKind.ToggleX11Density),
        _ => new FrameAction(FrameActionKind.ToggleMaximize),
    };

    private const string DensityLow = "Use low density";

    private const string DensityFull = "Use full density";

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
                return (state.Maximized ? "Restore" : "Maximize");
            }

            item--;
        }

        if (state.Capabilities.HasFlag(FrameCapabilities.X11Density))
        {
            if (item == 0)
            {
                return state.LowDensity ? DensityFull : DensityLow;
            }

            item--;
        }

        return "Close";
    }

    /// <summary>
    /// Names the resize cursor for an edge or a corner.
    /// </summary>
    /// <param name="part">The part under the pointer.</param>
    /// <returns>An X cursor name such as "top_side", or <see langword="null"/> for every other part.</returns>
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

    private void DrawChrome(SKCanvas canvas, in Box clientBox, in FrameState state, in FrameInteraction interaction, int iconScale)
    {
        _palette = Palette.For(state.ColorScheme, theme.HighContrast);
        var chrome = state.Active ? _palette.ChromeActive : _palette.ChromeInactive;
        var text = state.Active ? _palette.TextActive : _palette.TextInactive;
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
        stroke.Color = _palette.Outline;
        stroke.StrokeWidth = 1;
        canvas.DrawRect(0.5f, 0.5f, w - 1, h - 1, stroke);

        DrawIcon(canvas, state, text, iconScale);

        var titleRight = w - Border - 8;
        if (!_close.IsEmpty)
        {
            DrawButtonBack(canvas, _close, interaction, FramePart.Close, _palette.CloseHot, _palette.ClosePressed);
            stroke.Color = interaction.Hot == FramePart.Close ? _palette.TextActive : text;
            stroke.StrokeWidth = 1.4f;
            var c = Center(_close);
            canvas.DrawLine(c.X - 4, c.Y - 4, c.X + 4, c.Y + 4, stroke);
            canvas.DrawLine(c.X - 4, c.Y + 4, c.X + 4, c.Y - 4, stroke);
            titleRight = _close.X - 8;
        }

        if (!_maximize.IsEmpty)
        {
            DrawButtonBack(canvas, _maximize, interaction, FramePart.Maximize, _palette.ButtonHot, _palette.ButtonPressed);
            stroke.Color = interaction.Hot == FramePart.Maximize ? _palette.TextActive : text;
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

        if (!_minimize.IsEmpty)
        {
            DrawButtonBack(canvas, _minimize, interaction, FramePart.Minimize, _palette.ButtonHot, _palette.ButtonPressed);
            stroke.Color = interaction.Hot == FramePart.Minimize ? _palette.TextActive : text;
            stroke.StrokeWidth = 1.2f;
            var c = Center(_minimize);
            canvas.DrawLine(c.X - 4.5f, c.Y + 3.5f, c.X + 4.5f, c.Y + 3.5f, stroke);
            titleRight = _minimize.X - 8;
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

    private void DrawIcon(SKCanvas canvas, in FrameState state, SKColor text, int iconScale)
    {
        var box = new SKRect(_icon.X, _icon.Y, _icon.Right, _icon.Bottom);
        if (state.Icon.Pixels is { } pixels && theme.ImageFor(pixels) is { } clientIcon)
        {
            canvas.DrawImage(clientIcon, box, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return;
        }

        var iconName = state.Icon.Name is { Length: > 0 } named ? named : state.AppId;
        if (iconName is { Length: > 0 } && theme.IconFor(iconName, iconScale) is { } image)
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
