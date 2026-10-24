using System.Drawing;
using Basin.Capabilities;
using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <summary>
/// A reusable list of <see cref="MsStylesDrawOp"/> that a painter replays. A warm list allocates nothing, and each append method skips an operation that draws nothing.
/// </summary>
public sealed class MsStylesDrawList
{
    private MsStylesDrawOp[] _ops = new MsStylesDrawOp[64];

    /// <summary>
    /// Gets the number of operations in the list.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets an operation by its index.
    /// </summary>
    /// <param name="index">The index, below <see cref="Count"/>.</param>
    public ref readonly MsStylesDrawOp this[int index] => ref _ops[index];

    /// <summary>
    /// Gets the operations in draw order. The span is valid until the next append or <see cref="Clear"/>.
    /// </summary>
    public ReadOnlySpan<MsStylesDrawOp> Ops => _ops.AsSpan(0, Count);

    /// <summary>
    /// Removes every operation and keeps the storage.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_ops, 0, Count);
        Count = 0;
    }

    /// <summary>
    /// Appends an image operation. It adds nothing when the source or destination is empty or the opacity is 0 or less.
    /// </summary>
    /// <param name="bitmap">The <see cref="MsStylesBitmap.Handle"/> of the image.</param>
    /// <param name="frame">The image cell that holds the source.</param>
    /// <param name="source">The source area in texels.</param>
    /// <param name="destination">The area to draw in device pixels.</param>
    /// <param name="opacity">The opacity from 0 to 1.</param>
    /// <param name="tileX">Whether the image repeats across the destination.</param>
    /// <param name="tileY">Whether the image repeats down the destination.</param>
    /// <param name="tileScale">The device pixels for each texel of a tile.</param>
    /// <param name="smooth">Whether to sample linearly. When <see langword="null"/>, sampling is nearest-neighbor at a whole scale of 1 or more, and linear otherwise.</param>
    public void Image(int bitmap, Rectangle frame, RectangleF source, RectangleF destination, float opacity = 1f, bool tileX = false, bool tileY = false, float tileScale = 1f, bool? smooth = null)
    {
        if (destination.Width <= 0 || destination.Height <= 0 || source.Width <= 0 || source.Height <= 0 || opacity <= 0)
        {
            return;
        }

        ref var op = ref Add(MsStylesDrawOpKind.Image);
        op.Bitmap = bitmap;
        op.Source = source;
        op.Frame = frame;
        op.Destination = destination;
        op.Opacity = opacity;
        op.TileX = tileX;
        op.TileY = tileY;
        op.TileScale = tileScale;
        op.Smooth = smooth ?? !(IsIntegral(tileScale) && tileScale >= 1);
    }

    /// <summary>
    /// Appends a solid fill. It adds nothing for an empty area or a color with an alpha of 0.
    /// </summary>
    /// <param name="destination">The area to fill in device pixels.</param>
    /// <param name="argb">The color as straight 0xAARRGGBB.</param>
    public void Fill(RectangleF destination, uint argb)
    {
        if (destination.Width <= 0 || destination.Height <= 0 || (argb >> 24) == 0)
        {
            return;
        }

        ref var op = ref Add(MsStylesDrawOpKind.Fill);
        op.Destination = destination;
        op.Color = argb;
        op.Opacity = 1f;
    }

    /// <summary>
    /// Appends a stroke inside the destination. It adds nothing for an empty area or a size of 0 or less.
    /// </summary>
    /// <param name="destination">The outer edge of the stroke in device pixels.</param>
    /// <param name="size">The stroke width in device pixels.</param>
    /// <param name="argb">The color as straight 0xAARRGGBB.</param>
    /// <param name="shape">The shape to stroke.</param>
    /// <param name="radius">The corner radius in device pixels for a rounded rectangle.</param>
    public void Border(RectangleF destination, float size, uint argb, BorderType shape = BorderType.Rect, float radius = 0)
    {
        if (destination.Width <= 0 || destination.Height <= 0 || size <= 0)
        {
            return;
        }

        ref var op = ref Add(MsStylesDrawOpKind.Border);
        op.Destination = destination;
        op.Size = size;
        op.Color = argb;
        op.Shape = shape;
        op.Radius = radius;
        op.Opacity = 1f;
    }

    /// <summary>
    /// Appends a gradient fill. It adds nothing for an empty area or a gradient with no colors.
    /// </summary>
    /// <param name="destination">The area to fill in device pixels.</param>
    /// <param name="gradient">The gradient colors.</param>
    /// <param name="shape">The shape to fill.</param>
    /// <param name="radius">The corner radius in device pixels for a rounded rectangle.</param>
    public void Gradient(RectangleF destination, in MsStylesGradient gradient, BorderType shape = BorderType.Rect, float radius = 0)
    {
        if (destination.Width <= 0 || destination.Height <= 0 || gradient.Count == 0)
        {
            return;
        }

        ref var op = ref Add(MsStylesDrawOpKind.Gradient);
        op.Destination = destination;
        op.Gradient = gradient;
        op.Shape = shape;
        op.Radius = radius;
        op.Opacity = 1f;
    }

    /// <summary>
    /// Appends one line of text. It adds nothing for an empty text or area.
    /// </summary>
    /// <param name="text">The text. The painter cuts it with an ellipsis to fit.</param>
    /// <param name="font">The theme font.</param>
    /// <param name="pixels">The font size in device pixels.</param>
    /// <param name="destination">The text area in device pixels. The text is centered vertically in it.</param>
    /// <param name="align">The horizontal alignment.</param>
    /// <param name="argb">The text color as straight 0xAARRGGBB.</param>
    /// <param name="shadowArgb">The shadow color. An alpha of 0 draws no shadow.</param>
    /// <param name="shadowOffset">The shadow offset in device pixels.</param>
    public void Text(string text, in MsStylesFont font, float pixels, RectangleF destination, HorizontalAlign align, uint argb, uint shadowArgb = 0, PointF shadowOffset = default)
    {
        if (string.IsNullOrEmpty(text) || destination.Width <= 0 || destination.Height <= 0)
        {
            return;
        }

        ref var op = ref Add(MsStylesDrawOpKind.Text);
        op.Text = text;
        op.Font = font;
        op.FontPixels = pixels;
        op.Destination = destination;
        op.Align = align;
        op.Color = argb;
        op.ShadowColor = shadowArgb;
        op.ShadowOffset = shadowOffset;
        op.Opacity = 1f;
    }

    /// <summary>
    /// Appends a glow behind a text. It adds nothing for an empty text or area, or a size of 0 or less.
    /// </summary>
    /// <param name="text">The text that the glow follows.</param>
    /// <param name="font">The theme font.</param>
    /// <param name="pixels">The font size in device pixels.</param>
    /// <param name="destination">The text area in device pixels.</param>
    /// <param name="align">The horizontal alignment.</param>
    /// <param name="argb">The glow color as straight 0xAARRGGBB.</param>
    /// <param name="size">The glow size in device pixels.</param>
    /// <param name="intensity">The glow strength, where 1 is full strength.</param>
    public void Glow(string text, in MsStylesFont font, float pixels, RectangleF destination, HorizontalAlign align, uint argb, float size, float intensity)
    {
        if (string.IsNullOrEmpty(text) || destination.Width <= 0 || destination.Height <= 0 || size <= 0)
        {
            return;
        }

        ref var op = ref Add(MsStylesDrawOpKind.Glow);
        op.Text = text;
        op.Font = font;
        op.FontPixels = pixels;
        op.Destination = destination;
        op.Align = align;
        op.Color = argb;
        op.Size = size;
        op.Radius = intensity;
        op.Opacity = 1f;
    }

    /// <summary>
    /// Appends the window icon. It adds nothing for an empty area, or when the icon has no pixels or name and the application ID is empty.
    /// </summary>
    /// <param name="destination">The icon area in device pixels.</param>
    /// <param name="icon">The window icon.</param>
    /// <param name="appId">The application ID that names an icon when the window gives none.</param>
    /// <param name="opacity">The opacity from 0 to 1.</param>
    public void Icon(RectangleF destination, in FrameIcon icon, string? appId, float opacity = 1f)
    {
        if (destination.Width <= 0 || destination.Height <= 0 || (icon.Pixels is null && string.IsNullOrEmpty(icon.Name) && string.IsNullOrEmpty(appId)))
        {
            return;
        }

        ref var op = ref Add(MsStylesDrawOpKind.Icon);
        op.Destination = destination;
        op.Icon = icon;
        op.Text = appId;
        op.Opacity = opacity;
    }

    /// <summary>
    /// Appends a clip to an area. Each clip must have a matching <see cref="Restore"/>.
    /// </summary>
    /// <param name="destination">The clip area in device pixels.</param>
    public void Clip(RectangleF destination)
    {
        ref var op = ref Add(MsStylesDrawOpKind.Clip);
        op.Destination = destination;
    }

    /// <summary>
    /// Appends the end of the last <see cref="Clip"/>.
    /// </summary>
    public void Restore() => Add(MsStylesDrawOpKind.Restore);

    private ref MsStylesDrawOp Add(MsStylesDrawOpKind kind)
    {
        if (Count == _ops.Length)
        {
            Array.Resize(ref _ops, _ops.Length * 2);
        }

        ref var op = ref _ops[Count++];
        op = default;
        op.Kind = kind;
        return ref op;
    }

    private static bool IsIntegral(float value) => Math.Abs(value - MathF.Round(value)) < 0.001f;
}
