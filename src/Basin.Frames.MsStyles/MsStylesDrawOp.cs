using System.Drawing;
using Basin.Capabilities;
using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <summary>
/// One painter-neutral draw operation. <see cref="Kind"/> tells which fields apply. All coordinates are device pixels on the frame surface.
/// </summary>
public struct MsStylesDrawOp
{
    /// <summary>
    /// The kind of operation.
    /// </summary>
    public MsStylesDrawOpKind Kind;
    /// <summary>
    /// The <see cref="MsStylesBitmap.Handle"/> of the image to draw, for an image operation.
    /// </summary>
    public int Bitmap;
    /// <summary>
    /// The source area in bitmap texels, for an image operation.
    /// </summary>
    public RectangleF Source;
    /// <summary>
    /// The image cell that holds <see cref="Source"/>. A painter must not sample outside it, so the next cell does not bleed in.
    /// </summary>
    public Rectangle Frame;
    /// <summary>
    /// The area to draw, in device pixels. For a clip, the area to clip to.
    /// </summary>
    public RectangleF Destination;
    /// <summary>
    /// The opacity from 0 to 1.
    /// </summary>
    public float Opacity;
    /// <summary>
    /// A value indicating whether the image repeats across the destination instead of a stretch.
    /// </summary>
    public bool TileX;
    /// <summary>
    /// A value indicating whether the image repeats down the destination instead of a stretch.
    /// </summary>
    public bool TileY;
    /// <summary>
    /// The device pixels for each texel of a tile.
    /// </summary>
    public float TileScale;
    /// <summary>
    /// A value indicating whether the image is sampled linearly. When false, the painter uses nearest-neighbor sampling.
    /// </summary>
    public bool Smooth;
    /// <summary>
    /// The color as straight 0xAARRGGBB, for a fill, border, text or glow operation.
    /// </summary>
    public uint Color;
    /// <summary>
    /// The stroke width of a border, or the glow size of a glow, in device pixels.
    /// </summary>
    public float Size;
    /// <summary>
    /// The corner radius of a rounded border or gradient in device pixels. For a glow operation, the glow intensity, where 1 is full strength.
    /// </summary>
    public float Radius;
    /// <summary>
    /// The shape of a border or gradient operation.
    /// </summary>
    public BorderType Shape;
    /// <summary>
    /// The colors of a gradient operation.
    /// </summary>
    public MsStylesGradient Gradient;
    /// <summary>
    /// The text of a text or glow operation. For an icon operation, the application ID that names a fallback icon.
    /// </summary>
    public string? Text;
    /// <summary>
    /// The theme font of a text or glow operation.
    /// </summary>
    public MsStylesFont Font;
    /// <summary>
    /// The font size in device pixels.
    /// </summary>
    public float FontPixels;
    /// <summary>
    /// The horizontal alignment of the text in the destination.
    /// </summary>
    public HorizontalAlign Align;
    /// <summary>
    /// The text shadow color as straight 0xAARRGGBB. An alpha of 0 draws no shadow.
    /// </summary>
    public uint ShadowColor;
    /// <summary>
    /// The text shadow offset in device pixels.
    /// </summary>
    public PointF ShadowOffset;
    /// <summary>
    /// The window icon of an icon operation.
    /// </summary>
    public FrameIcon Icon;
}
