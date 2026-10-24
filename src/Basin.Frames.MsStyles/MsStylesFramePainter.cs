using System.Drawing;
using Basin.Capabilities;
using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <summary>
/// Lays out one window's frame in logical pixels, emits its draw list in device pixels, and answers hit tests. It keeps per-window state, so each window needs its own instance.
/// </summary>
public sealed partial class MsStylesFramePainter
{
    /// <summary>
    /// The length in logical pixels along each edge from a corner where a hit test reports the corner.
    /// </summary>
    public const int CornerZone = 16;

    private const int ButtonNormal = 1;
    private const int ButtonHot = 2;
    private const int ButtonPushed = 3;
    private const int ButtonDisabled = 4;

    private readonly MsStylesFrameStyle _style;
    private int _chromeOps;
    private double _paintedScale = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesFramePainter"/> class.
    /// </summary>
    /// <param name="style">The shared frame style.</param>
    public MsStylesFramePainter(MsStylesFrameStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
    }

    /// <summary>
    /// Gets the shared frame style.
    /// </summary>
    public MsStylesFrameStyle Style => _style;

    /// <summary>
    /// Gets the layout that the last <see cref="Layout"/> call wrote. The instance stays the same for the life of the painter.
    /// </summary>
    public MsStylesFrameGeometry Geometry { get; } = new();

    /// <summary>
    /// Gets the draw list that the last <see cref="Paint"/> call wrote, in device pixels.
    /// </summary>
    public MsStylesDrawList List { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the last layout produced a frame. It is false for a fullscreen window or an empty surface.
    /// </summary>
    public bool LaidOut { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the chrome of the last paint has no transparent image. It is false for the glass look, except for a maximized window.
    /// </summary>
    public bool Opaque { get; private set; }

    /// <summary>
    /// Measures the frame thickness around the client area. A fullscreen window has none, and a maximized window has only the caption.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <returns>The insets in logical pixels.</returns>
    public FrameInsets Measure(in FrameState state)
    {
        if (state.Fullscreen)
        {
            return default;
        }

        var border = _style.Border;
        if (state.Kind == FrameKind.Border)
        {
            return new FrameInsets(border, border, border, border);
        }

        if (state.Maximized)
        {
            return new FrameInsets(_style.CaptionBar + 1, 0, 0, 0);
        }

        var bar = state.Kind == FrameKind.Utility ? _style.SmallCaptionBar : _style.CaptionBar;
        return new FrameInsets(border + bar + 1, border, border, border);
    }

    /// <summary>
    /// Measures the shadow margin outside the window. Only a glass frame that is not maximized, fullscreen or border-only has one.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <returns>The outsets in logical pixels.</returns>
    public FrameInsets Outsets(in FrameState state)
    {
        if (!_style.IsGlass || state.Fullscreen || state.Maximized || state.Kind == FrameKind.Border)
        {
            return default;
        }

        return new FrameInsets(VistaButtonMetrics.ShadowTop, VistaButtonMetrics.ShadowRight, VistaButtonMetrics.ShadowBottom, VistaButtonMetrics.ShadowLeft);
    }

    /// <summary>
    /// Lays out the frame around a client area and writes the result to <see cref="Geometry"/>.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="clientWidth">The client width in logical pixels.</param>
    /// <param name="clientHeight">The client height in logical pixels.</param>
    /// <returns><see langword="true"/> when the frame has an area to draw. It is the new value of <see cref="LaidOut"/>.</returns>
    public bool Layout(in FrameState state, int clientWidth, int clientHeight)
    {
        var insets = Measure(state);
        var outsets = Outsets(state);
        var geometry = Geometry;
        geometry.Insets = insets;
        geometry.Outsets = outsets;
        var windowWidth = clientWidth + insets.Left + insets.Right;
        var windowHeight = clientHeight + insets.Top + insets.Bottom;
        geometry.Width = windowWidth + outsets.Left + outsets.Right;
        geometry.Height = windowHeight + outsets.Top + outsets.Bottom;
        geometry.WindowRect = new RectangleF(outsets.Left, outsets.Top, windowWidth, windowHeight);
        geometry.ClientRect = new RectangleF(outsets.Left + insets.Left, outsets.Top + insets.Top, clientWidth, clientHeight);
        geometry.ButtonCount = 0;
        geometry.IconRect = RectangleF.Empty;
        geometry.TitleRect = RectangleF.Empty;
        LaidOut = geometry.Width > 0 && geometry.Height > 0 && !state.Fullscreen;
        if (!LaidOut)
        {
            return false;
        }

        var border = _style.Border;
        var small = state.Kind == FrameKind.Utility;
        var maximized = state.Maximized;
        var bar = small ? _style.SmallCaptionBar : _style.CaptionBar;
        var caption = border + bar + 1;
        geometry.Border = border;
        geometry.Small = small;
        geometry.Maximized = maximized;
        geometry.HasCaption = state.Kind != FrameKind.Border;
        geometry.CaptionHeight = geometry.HasCaption ? (maximized ? bar + 1 : caption) : border;

        var shift = maximized ? border : 0;
        var x0 = (float)outsets.Left;
        var y0 = (float)outsets.Top;
        var width = (float)windowWidth;
        var height = (float)windowHeight;
        var left = x0 - shift;
        var top = y0 - shift;
        var right = x0 + width + shift;
        geometry.CaptionRect = maximized
            ? new RectangleF(x0, y0, width, bar + 1)
            : new RectangleF(x0, y0, width, geometry.HasCaption ? caption : border);
        var frameTop = y0 + (geometry.HasCaption ? caption : border);
        geometry.LeftRect = maximized ? RectangleF.Empty : RectangleF.FromLTRB(x0, frameTop, x0 + border, y0 + height - border);
        geometry.RightRect = maximized ? RectangleF.Empty : RectangleF.FromLTRB(x0 + width - border, frameTop, x0 + width, y0 + height - border);
        geometry.BottomRect = maximized ? RectangleF.Empty : RectangleF.FromLTRB(x0, y0 + height - border, x0 + width, y0 + height);
        if (!geometry.HasCaption)
        {
            return true;
        }

        if (_style.IsGlass)
        {
            LayoutGlassButtons(state, geometry, x0, y0, x0 + width, small);
        }
        else
        {
            LayoutButtons(state, geometry, left, top, right, border, small);
        }

        var hasIcon = state.Kind == FrameKind.Normal && !small &&
                      (state.Icon.Pixels is not null || !string.IsNullOrEmpty(state.Icon.Name) || !string.IsNullOrEmpty(state.AppId));
        var iconSize = MsStylesFrameStyle.IconSize;
        var textTop = top + border;
        var textBottom = top + caption;
        if (_style.IsGlass && !small)
        {
            textTop -= VistaButtonMetrics.GlassTitleRaise;
            textBottom -= VistaButtonMetrics.GlassTitleRaise;
        }

        if (hasIcon)
        {
            var iconLeft = _style.IsGlass ? left + border : left + border + 2;
            geometry.IconRect = new RectangleF(iconLeft, textTop + MathF.Floor((textBottom - textTop - iconSize) / 2f), iconSize, iconSize);
        }

        var captionPart = _style.CaptionPart(small, maximized);
        var margins = TextClass.TryGetMargins(captionPart, state.Active ? 1 : 2, ThemeProperty.CaptionMargins, out var captionMargins)
            ? captionMargins
            : default;
        var textLeft = (hasIcon ? geometry.IconRect.Right : left + border) + margins.Left;
        var textRight = right - border - margins.Right;
        foreach (ref readonly var button in geometry.Buttons)
        {
            textRight = MathF.Min(textRight, button.Bounds.Left - margins.Right);
        }

        geometry.TitleRect = RectangleF.FromLTRB(textLeft, textTop, MathF.Max(textLeft, textRight), textBottom);
        return true;
    }

    private void LayoutButtons(in FrameState state, MsStylesFrameGeometry geometry, float left, float top, float right, int border, bool small)
    {
        var width = small ? _style.SmallButtonWidth : _style.ButtonWidth;
        var height = small ? _style.SmallButtonHeight : _style.ButtonHeight;
        var edge = border + VistaButtonMetrics.EdgeBeyondFrame;
        var x = right - edge;
        var y = top + edge;
        var count = 0;

        void Add(FramePart part, WindowPart windowPart, bool enabled)
        {
            x -= width;
            ref var button = ref geometry.Button(count++);
            button.Part = part;
            button.WindowPart = (int)windowPart;
            button.Bounds = new RectangleF(x, y, width, height);
            button.Enabled = enabled;
            x -= VistaButtonMetrics.Gap;
        }

        Add(FramePart.Close, small ? WindowPart.SmallCloseButton : WindowPart.CloseButton, true);
        if (state.Kind == FrameKind.Normal && !small)
        {
            var minimize = (state.Capabilities & FrameCapabilities.Minimize) != 0;
            var maximize = (state.Capabilities & FrameCapabilities.Maximize) != 0;
            if (minimize || maximize)
            {
                Add(FramePart.Maximize, state.Maximized ? WindowPart.RestoreButton : WindowPart.MaxButton, maximize);
                Add(FramePart.Minimize, WindowPart.MinButton, minimize);
            }
        }

        geometry.ButtonCount = count;
    }

    /// <summary>
    /// Clears <see cref="List"/> and writes the operations for the last layout. It writes nothing when <see cref="LaidOut"/> is false.
    /// </summary>
    /// <param name="state">The window state. It must be the state of the last layout.</param>
    /// <param name="interaction">The pointer state and the change times that select the button states.</param>
    /// <param name="scale">The output scale from logical to device pixels.</param>
    public void Paint(in FrameState state, in FrameInteraction interaction, double scale)
    {
        var list = List;
        list.Clear();
        _paintedScale = scale;
        Opaque = false;
        if (!LaidOut)
        {
            return;
        }

        var geometry = Geometry;
        if (_style.IsGlass)
        {
            PaintGlass(state, interaction, scale);
            return;
        }

        var window = _style.Window;
        var context = new MsStylesDrawContext(_style.Images, 96 * scale);
        var active = state.Active;
        var frameState = active ? 1 : 2;
        var captionPart = _style.CaptionPart(geometry.Small, geometry.Maximized);
        if (geometry.HasCaption)
        {
            MsStylesDrawing.Background(list, window, captionPart, frameState, Device(geometry.CaptionRect, scale), context);
        }
        else
        {
            MsStylesDrawing.Background(list, window, (int)WindowPart.Caption, frameState, Device(geometry.CaptionRect, scale), context);
        }

        if (!geometry.Maximized)
        {
            MsStylesDrawing.Background(list, window, _style.FramePart(WindowPart.FrameLeft, WindowPart.SmallFrameLeft, geometry.Small), frameState, Device(geometry.LeftRect, scale), context);
            MsStylesDrawing.Background(list, window, _style.FramePart(WindowPart.FrameRight, WindowPart.SmallFrameRight, geometry.Small), frameState, Device(geometry.RightRect, scale), context);
            MsStylesDrawing.Background(list, window, _style.FramePart(WindowPart.FrameBottom, WindowPart.SmallFrameBottom, geometry.Small), frameState, Device(geometry.BottomRect, scale), context);
        }

        _chromeOps = list.Count;
        Opaque = !AnyTransparent(list, 0, _chromeOps);
        if (!geometry.HasCaption)
        {
            return;
        }

        if (!geometry.IconRect.IsEmpty)
        {
            var icon = Device(geometry.IconRect, scale);
            if (window.HasPart((int)WindowPart.SysButton))
            {
                MsStylesDrawing.Background(list, window, (int)WindowPart.SysButton, active ? ButtonNormal : ButtonDisabled, icon, context);
            }

            list.Icon(icon, state.Icon, state.AppId);
        }

        PaintTitle(state, captionPart, frameState, scale);
        foreach (ref readonly var button in geometry.Buttons)
        {
            var buttonState = !button.Enabled ? ButtonDisabled
                : interaction.Pressed == button.Part && interaction.Hot == button.Part ? ButtonPushed
                : interaction.Hot == button.Part && interaction.Pressed == FramePart.None ? ButtonHot
                : ButtonNormal;
            var frames = window.TryGetInt(button.WindowPart, buttonState, ThemeProperty.ImageCount, out var count) ? count : 1;
            var frame = frames >= 8 && !active ? buttonState + 4 : buttonState;
            MsStylesDrawing.Background(list, window, button.WindowPart, buttonState, Device(button.Bounds, scale), context, frame);
        }
    }

    private MsStylesClass TextClass => _style.IsGlass ? _style.CompositedWindow : _style.Window;

    private void PaintTitle(in FrameState state, int captionPart, int captionState, double scale)
    {
        if (string.IsNullOrEmpty(state.Title) || Geometry.TitleRect.Width <= 0)
        {
            return;
        }

        var window = TextClass;
        var color = window.TryGetColor(captionPart, captionState, ThemeProperty.TextColor, out var themed) &&
                    window.OriginOf(captionPart, captionState, ThemeProperty.TextColor) != PropertyOrigin.Global
            ? themed
            : state.Active ? _style.CaptionText : _style.InactiveCaptionText;
        uint shadow = 0;
        var shadowOffset = PointF.Empty;
        if (window.TryGetEnum<TextShadowType>(captionPart, captionState, ThemeProperty.TextShadowType, out var shadowType) &&
            shadowType == TextShadowType.Single &&
            window.TryGetColor(captionPart, captionState, ThemeProperty.TextShadowColor, out var shadowColor))
        {
            shadow = shadowColor.ToArgb();
            var offset = window.TryGetPosition(captionPart, captionState, ThemeProperty.TextShadowOffset, out var position) ? position : new MsStylesPosition(1, 1);
            shadowOffset = new PointF((float)Math.Round(offset.X * scale), (float)Math.Round(offset.Y * scale));
        }

        var align = window.TryGetEnum<ContentAlignment>(captionPart, captionState, ThemeProperty.ContentAlignment, out var alignment)
            ? (HorizontalAlign)alignment
            : HorizontalAlign.Left;
        var font = Geometry.Small ? _style.SmallCaptionFont : _style.CaptionFont;
        var pixels = (float)(font.Points * 96.0 / 72.0 * scale);
        if (_style.IsGlass && !Geometry.Maximized &&
            window.TryGetInt(captionPart, captionState, ThemeProperty.TextGlowSize, out var glowSize) && glowSize > 0)
        {
            var glow = window.TryGetColor(captionPart, captionState, ThemeProperty.GlowColor, out var glowColor) ? glowColor : new MsStylesColor(255, 255, 255);
            var intensity = window.TryGetInt(captionPart, captionState, ThemeProperty.GlowIntensity, out var glowIntensity) ? glowIntensity : 100;
            List.Glow(state.Title!, font, pixels, Device(Geometry.TitleRect, scale), align, glow.ToArgb(), (float)(glowSize * scale), Math.Clamp(intensity, 0, 1000) / 100f);
        }

        List.Text(state.Title, font, pixels, Device(Geometry.TitleRect, scale), align, color.ToArgb(), shadow, shadowOffset);
    }

    /// <summary>
    /// Finds the frame part at a point. A point over a fully transparent pixel of the last paint gives <see cref="FramePart.None"/>.
    /// </summary>
    /// <param name="x">The horizontal position in logical pixels on the frame surface.</param>
    /// <param name="y">The vertical position in logical pixels on the frame surface.</param>
    /// <returns>The part, or <see cref="FramePart.None"/> outside the window or before a layout.</returns>
    public FramePart PartAt(double x, double y)
    {
        if (!LaidOut)
        {
            return FramePart.None;
        }

        var part = HitPart(x, y);
        if (part != FramePart.None && IsTransparentAt(x, y))
        {
            return FramePart.None;
        }

        return part;
    }

    private FramePart HitPart(double x, double y)
    {
        var geometry = Geometry;
        var windowRect = geometry.WindowRect;
        if (x < windowRect.Left || y < windowRect.Top || x >= windowRect.Right || y >= windowRect.Bottom)
        {
            return FramePart.None;
        }

        var button = geometry.ButtonAt(x, y);
        if (button != FramePart.None)
        {
            return button;
        }

        if (!geometry.IconRect.IsEmpty && geometry.IconRect.Contains((float)x, (float)y))
        {
            return FramePart.Menu;
        }

        var insets = geometry.Insets;
        x -= windowRect.Left;
        y -= windowRect.Top;
        var w = windowRect.Width;
        var h = windowRect.Height;
        if (geometry.Maximized)
        {
            return y < insets.Top ? FramePart.Title : FramePart.None;
        }

        var border = geometry.Border;
        var nearLeft = x < CornerZone;
        var nearRight = x >= w - CornerZone;
        var nearTop = y < CornerZone;
        var nearBottom = y >= h - CornerZone;
        if (y < border)
        {
            return nearLeft ? FramePart.TopLeft : nearRight ? FramePart.TopRight : FramePart.Top;
        }

        if (x < border)
        {
            return nearTop ? FramePart.TopLeft : nearBottom ? FramePart.BottomLeft : FramePart.Left;
        }

        if (x >= w - border)
        {
            return nearTop ? FramePart.TopRight : nearBottom ? FramePart.BottomRight : FramePart.Right;
        }

        if (y >= h - border)
        {
            return nearLeft ? FramePart.BottomLeft : nearRight ? FramePart.BottomRight : FramePart.Bottom;
        }

        if (y < insets.Top)
        {
            return geometry.HasCaption ? FramePart.Title : FramePart.Top;
        }

        return FramePart.Border;
    }

    /// <summary>
    /// Tells whether a caption button hover is still fading. Only the glass look animates.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="interaction">The pointer state and the change times.</param>
    /// <returns><see langword="true"/> while a glass button fades in or out.</returns>
    public bool Animating(in FrameState state, in FrameInteraction interaction) =>
        _style.IsGlass && LaidOut && GlassAnimating(interaction);

    /// <summary>
    /// Gives the area that a caption button covers, including its glow for the glass look.
    /// </summary>
    /// <param name="part">The part to look up. Only close, maximize and minimize have bounds.</param>
    /// <returns>The area in logical pixels, clipped to the surface, or an empty box for any other part.</returns>
    public Basin.Box PartBounds(FramePart part)
    {
        if (!LaidOut)
        {
            return default;
        }

        var bounds = part is FramePart.Close or FramePart.Maximize or FramePart.Minimize ? Geometry.BoundsOf(part) : RectangleF.Empty;
        if (bounds.IsEmpty)
        {
            return default;
        }

        if (_style.IsGlass)
        {
            foreach (ref readonly var button in Geometry.Buttons)
            {
                if (button.Part == part)
                {
                    bounds = GlowBounds(_style.DwmWindow!, button);
                    break;
                }
            }
        }

        var left = (int)MathF.Floor(MathF.Max(0, bounds.Left));
        var top = (int)MathF.Floor(MathF.Max(0, bounds.Top));
        var right = (int)MathF.Ceiling(MathF.Min(Geometry.Width, bounds.Right));
        var bottom = (int)MathF.Ceiling(MathF.Min(Geometry.Height, bounds.Bottom));
        return right > left && bottom > top ? new Basin.Box(left, top, right - left, bottom - top) : default;
    }

    /// <summary>
    /// Tells whether the last paint left a point fully transparent. It samples the topmost image under the point. A fill, text or icon counts as opaque.
    /// </summary>
    /// <param name="x">The horizontal position in logical pixels on the frame surface.</param>
    /// <param name="y">The vertical position in logical pixels on the frame surface.</param>
    /// <returns><see langword="true"/> when no operation covers the point or the image texel there has an alpha of 0.</returns>
    public bool IsTransparentAt(double x, double y)
    {
        var px = (float)(x * _paintedScale);
        var py = (float)(y * _paintedScale);
        var ops = List.Ops;
        for (var i = ops.Length - 1; i >= 0; i--)
        {
            ref readonly var op = ref ops[i];
            var d = op.Destination;
            if (op.Kind is MsStylesDrawOpKind.Clip or MsStylesDrawOpKind.Restore || px < d.Left || px >= d.Right || py < d.Top || py >= d.Bottom)
            {
                continue;
            }

            if (op.Kind != MsStylesDrawOpKind.Image)
            {
                return false;
            }

            var bitmap = _style.Images[op.Bitmap];
            if (!bitmap.HasAlpha)
            {
                return false;
            }

            float u;
            float v;
            if (op.TileX || op.TileY)
            {
                u = op.Source.X + ((px - d.Left) / op.TileScale) % op.Source.Width;
                v = op.Source.Y + ((py - d.Top) / op.TileScale) % op.Source.Height;
            }
            else
            {
                u = op.Source.X + (px - d.Left) * op.Source.Width / d.Width;
                v = op.Source.Y + (py - d.Top) * op.Source.Height / d.Height;
            }

            var sx = Math.Clamp((int)u, 0, bitmap.Width - 1);
            var sy = Math.Clamp((int)v, 0, bitmap.Height - 1);
            if ((bitmap.PixelAt(sx, sy) >> 24) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private bool AnyTransparent(MsStylesDrawList list, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (list[i].Kind == MsStylesDrawOpKind.Image && _style.Images[list[i].Bitmap].HasAlpha)
            {
                return true;
            }
        }

        return false;
    }

    internal static RectangleF Device(RectangleF logical, double scale) =>
        RectangleF.FromLTRB(
            (float)Math.Round(logical.Left * scale),
            (float)Math.Round(logical.Top * scale),
            (float)Math.Round(logical.Right * scale),
            (float)Math.Round(logical.Bottom * scale));
}
