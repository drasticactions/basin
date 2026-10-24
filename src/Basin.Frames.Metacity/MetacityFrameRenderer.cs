using Basin.Capabilities;
using Basin.UI.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.Frames.Metacity;

/// <summary>
/// An <see cref="IFrameRenderer"/> that paints a metacity theme through SkiaSharp on an <see cref="ISkiaUISurface"/>, and draws marco's window menu.
/// It keeps the layout of one window, so each window must have its own instance.
/// </summary>
public sealed partial class MetacityFrameRenderer : IFrameRenderer
{
    /// <summary>
    /// Gets the <see cref="ISkiaUISurface"/> type. A UI host that cannot give a Skia surface cannot host this renderer.
    /// </summary>
    public Type SurfaceContract => typeof(ISkiaUISurface);

    private const int CornerZone = 16;
    private const int TopResizeHeight = 4;

    private readonly MetacityPainter _base;
    private MetacityPainter _painter;
    private MetacityPainter? _light;
    private MetacityPainter? _dark;
    private readonly MetacityFrameGeometry _geometry = new();
    private bool _laidOut;
    private bool _shaded;
    private bool _translucent;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetacityFrameRenderer"/> class over a painter.
    /// </summary>
    /// <param name="painter">The painter for the default color scheme. The renderer makes light and dark painters from it when a window asks for them.</param>
    public MetacityFrameRenderer(MetacityPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        _base = painter;
        _painter = painter;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetacityFrameRenderer"/> class with a new <see cref="MetacityPainter"/>.
    /// </summary>
    /// <param name="theme">The loaded theme.</param>
    /// <param name="palette">The colors for <c>gtk:</c> specs.</param>
    /// <param name="buttonLayout">The button layout.</param>
    /// <param name="font">The title font.</param>
    /// <param name="resources">The shared Skia resources.</param>
    public MetacityFrameRenderer(MetacityTheme theme, MetacityPalette palette, MetacityButtonLayout buttonLayout, MetacityFont font, MetacityResources resources)
        : this(new MetacityPainter(theme, palette, buttonLayout, font, resources))
    {
    }

    /// <summary>
    /// Gets the painter for the default color scheme, the one given at construction.
    /// </summary>
    public MetacityPainter Painter => _base;

    private MetacityPainter PainterFor(FrameColorScheme scheme) => scheme switch
    {
        FrameColorScheme.Light => _light ??= new MetacityPainter(_base.Theme, MetacityPalette.Light, _base.ButtonLayout, _base.Font, _base.Resources),
        FrameColorScheme.Dark => _dark ??= new MetacityPainter(_base.Theme, MetacityPalette.Dark, _base.ButtonLayout, _base.Font, _base.Resources),
        _ => _base,
    };

    /// <summary>
    /// Gets the layout of the last draw.
    /// </summary>
    public MetacityFrameGeometry Geometry => _geometry;

    /// <summary>
    /// Gets or sets a value indicating whether the frame asks for a backdrop effect. When true, <see cref="BackdropRegion"/> fills the frame outline and <see cref="OpaqueChrome"/> is false.
    /// </summary>
    public bool Frosted { get; set; }

    /// <summary>
    /// Gets a value indicating whether every pixel of the frame is opaque. It is true before the first draw. After a draw it is false for rounded corners,
    /// a shaded window, a frosted frame or a translucent window background.
    /// </summary>
    public bool OpaqueChrome => !_laidOut || (!_geometry.HasRoundedCorner && !_shaded && !Frosted && !_translucent);

    /// <summary>
    /// Fills the frame outline, with each rounded corner cut by marco's curve, when <see cref="Frosted"/> is true.
    /// </summary>
    /// <param name="state">The window state.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="into">The region to fill in logical surface coordinates. The method clears it first.</param>
    /// <returns><see langword="true"/> for a frosted frame that has been laid out.</returns>
    public bool BackdropRegion(in FrameState state, double scale, PixmanRegion32 into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (!Frosted || !_laidOut)
        {
            return false;
        }

        Span<int> topLeft = stackalloc int[Math.Clamp(_geometry.TopLeftRadius, 0, MaxCornerRows)];
        Span<int> topRight = stackalloc int[Math.Clamp(_geometry.TopRightRadius, 0, MaxCornerRows)];
        Span<int> bottomLeft = stackalloc int[Math.Clamp(_geometry.BottomLeftRadius, 0, MaxCornerRows)];
        Span<int> bottomRight = stackalloc int[Math.Clamp(_geometry.BottomRightRadius, 0, MaxCornerRows)];
        MarcoCurve(_geometry.TopLeftRadius, topLeft);
        MarcoCurve(_geometry.TopRightRadius, topRight);
        MarcoCurve(_geometry.BottomLeftRadius, bottomLeft);
        MarcoCurve(_geometry.BottomRightRadius, bottomRight);
        return RoundedRegion.Fill(into, _geometry.Width, _geometry.Height, topLeft, topRight, bottomLeft, bottomRight);
    }

    private const int MaxCornerRows = 64;

    private static void MarcoCurve(int corner, Span<int> rows)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = RoundedRegion.MarcoInset(corner, i);
        }
    }

    /// <inheritdoc/>
    public FrameInsets Measure(in FrameState state, double scale) =>
        _painter.Measure(new MetacityFrameInput(state, default));

    /// <inheritdoc/>
    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var skia = (ISkiaUISurface)surface;
        _painter = PainterFor(state.ColorScheme);
        var input = new MetacityFrameInput(state, interaction);
        _laidOut = _painter.Layout(input, clientBox.Width, clientBox.Height, _geometry);
        _shaded = state.Shaded;
        _translucent = _painter.StyleFor(input) is { WindowBackground: not null, WindowBackgroundAlpha: < 255 };
        var canvas = skia.BeginDraw();
        try
        {
            canvas.Clear(SKColors.Transparent);
            if (_laidOut)
            {
                _painter.Paint(canvas, input, _geometry, surface.Size.Scale);
            }
        }
        finally
        {
            skia.EndDraw();
        }
    }

    /// <inheritdoc/>
    public FramePart PartAt(double x, double y, in FrameState state, double scale)
    {
        if (!_laidOut)
        {
            return FramePart.None;
        }

        var w = _geometry.Width;
        var h = _geometry.Height;
        if (w <= 0 || h <= 0 || x < 0 || y < 0 || x >= w || y >= h)
        {
            return FramePart.None;
        }

        var borders = _geometry.Borders;
        var nearTop = y < CornerZone;
        var nearLeft = x < borders.Left + CornerZone;
        var nearRight = x >= w - borders.Right - CornerZone;
        var onTopRing = y < TopResizeHeight;
        if (nearTop && nearLeft && (onTopRing || x < Math.Max(borders.Left, TopResizeHeight)))
        {
            return FramePart.TopLeft;
        }

        if (nearTop && nearRight && (onTopRing || x >= w - Math.Max(borders.Right, TopResizeHeight)))
        {
            return FramePart.TopRight;
        }

        if (_geometry.ButtonAt(x, y) is var button && button != FramePart.None)
        {
            return button;
        }

        if (y < borders.Top)
        {
            if (_geometry.TitleRect.Contains(new Point((int)x, (int)y)))
            {
                return onTopRing ? FramePart.Top : FramePart.Title;
            }

            if (nearTop && nearLeft)
            {
                return FramePart.TopLeft;
            }

            if (nearTop && nearRight)
            {
                return FramePart.TopRight;
            }

            if (onTopRing)
            {
                return FramePart.Top;
            }

            return nearLeft ? FramePart.Left : nearRight ? FramePart.Right : FramePart.Title;
        }

        var onLeft = x < borders.Left;
        var onRight = x >= w - borders.Right;
        var onBottom = y >= h - borders.Bottom;
        if (!onLeft && !onRight && !onBottom)
        {
            return FramePart.Border;
        }

        nearTop = y < borders.Top + CornerZone;
        var nearBottom = y >= h - Math.Max(borders.Bottom, CornerZone);
        nearLeft = x < Math.Max(borders.Left, CornerZone);
        nearRight = x >= w - Math.Max(borders.Right, CornerZone);
        if (nearTop && nearLeft)
        {
            return FramePart.TopLeft;
        }

        if (nearTop && nearRight)
        {
            return FramePart.TopRight;
        }

        if (nearBottom && nearLeft)
        {
            return FramePart.BottomLeft;
        }

        if (nearBottom && nearRight)
        {
            return FramePart.BottomRight;
        }

        return onBottom ? FramePart.Bottom : onLeft ? FramePart.Left : FramePart.Right;
    }

    /// <summary>
    /// Gives the drawn area of a button in the last draw. A hover or press change on the button damages only that area.
    /// </summary>
    /// <param name="part">The part to look up.</param>
    /// <returns>The button area in surface coordinates, or an empty box for a part that is not a shown button.</returns>
    public Box PartBounds(FramePart part) => _laidOut ? _geometry.VisibleRect(part) : default;

    /// <inheritdoc/>
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
}
