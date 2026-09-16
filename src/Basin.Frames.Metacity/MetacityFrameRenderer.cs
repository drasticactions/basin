using Basin.Capabilities;
using Basin.UI.Skia;
using SkiaSharp;

namespace Basin.Frames.Metacity;

public sealed partial class MetacityFrameRenderer : IFrameRenderer
{
    private const int CornerZone = 16;
    private const int TopResizeHeight = 4;

    private readonly MetacityPainter _painter;
    private readonly MetacityFrameGeometry _geometry = new();
    private bool _laidOut;
    private bool _shaded;

    public MetacityFrameRenderer(MetacityPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        _painter = painter;
    }

    public MetacityFrameRenderer(MetacityTheme theme, MetacityPalette palette, MetacityButtonLayout buttonLayout, MetacityFont font, MetacityResources resources)
        : this(new MetacityPainter(theme, palette, buttonLayout, font, resources))
    {
    }

    public MetacityPainter Painter => _painter;

    public MetacityFrameGeometry Geometry => _geometry;

    public bool OpaqueChrome => !_laidOut || (!_geometry.HasRoundedCorner && !_shaded);

    public FrameInsets Measure(in FrameState state, double scale) =>
        _painter.Measure(new MetacityFrameInput(state, default));

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var skia = (ISkiaUISurface)surface;
        var input = new MetacityFrameInput(state, interaction);
        _laidOut = _painter.Layout(input, clientBox.Width, clientBox.Height, _geometry);
        _shaded = state.Shaded;
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

    public Box PartBounds(FramePart part) => _laidOut ? _geometry.VisibleRect(part) : default;

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
