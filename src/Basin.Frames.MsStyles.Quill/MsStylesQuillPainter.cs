using System.Drawing;
using System.Runtime.CompilerServices;
using Basin.Themes.MsStyles;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Spatial;
using SkiaSharp;

namespace Basin.Frames.MsStyles.Quill;

/// <summary>
/// Replays an <see cref="MsStylesDrawList"/> on a Prowl.Quill canvas. It caches textures for each canvas renderer, so many windows can share one instance.
/// </summary>
public sealed class MsStylesQuillPainter
{
    private const int MaxNearestScale = 4;

    private readonly ConditionalWeakTable<ICanvasRenderer, MsStylesQuillTextures> _textures = [];
    private readonly Dictionary<IBuffer, (int Width, int Height, byte[] Rgba)?> _pixelIcons = [];
    private readonly Dictionary<SKImage, (int Width, int Height, byte[] Rgba)?> _namedIcons = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesQuillPainter"/> class.
    /// </summary>
    /// <param name="images">The decoded theme images that the draw lists refer to by handle.</param>
    /// <param name="text">The text engine that draws and measures titles.</param>
    public MsStylesQuillPainter(MsStylesImageCache images, MsStylesQuillText text)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(text);
        Images = images;
        Text = text;
    }

    /// <summary>
    /// Gets the image cache that resolves the bitmap handles in a draw list.
    /// </summary>
    public MsStylesImageCache Images { get; }

    /// <summary>
    /// Gets the text engine that draws text and glow operations.
    /// </summary>
    public MsStylesQuillText Text { get; }

    /// <summary>
    /// Gets or sets the lookup for named icons. It takes an icon name and a scale in multiples of 16 pixels, and returns
    /// <see langword="null"/> when no icon matches. The caller keeps ownership of each returned image.
    /// </summary>
    public Func<string, int, SKImage?>? Icons { get; set; }

    /// <summary>
    /// Draws every operation of a draw list. The list is in device pixels, and the canvas is in logical pixels.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="renderer">The canvas renderer that owns the textures for this canvas.</param>
    /// <param name="list">The operations to draw.</param>
    /// <param name="scale">The output scale that converts device pixels to canvas units.</param>
    public void Paint(Canvas canvas, ICanvasRenderer renderer, MsStylesDrawList list, float scale)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(list);
        var textures = _textures.GetValue(renderer, static r => new MsStylesQuillTextures(r));
        var antiAlias = canvas.AntiAlias;
        canvas.SetAntiAlias(false);
        canvas.SetGlobalAlpha(1f);
        try
        {
            foreach (ref readonly var op in list.Ops)
            {
                switch (op.Kind)
                {
                    case MsStylesDrawOpKind.Image:
                        DrawImage(canvas, op, textures, scale);
                        break;
                    case MsStylesDrawOpKind.Fill:
                        var fill = Logical(op.Destination, scale);
                        canvas.RectFilled(fill.X, fill.Y, fill.Width, fill.Height, Color(op.Color));
                        break;
                    case MsStylesDrawOpKind.Border:
                        DrawBorder(canvas, op, scale);
                        break;
                    case MsStylesDrawOpKind.Gradient:
                        DrawGradient(canvas, op, textures, scale);
                        break;
                    case MsStylesDrawOpKind.Text:
                        Text.Draw(canvas, op, scale);
                        break;
                    case MsStylesDrawOpKind.Glow:
                        textures.Glow.Draw(canvas, renderer, Text.Raster, op, scale);
                        break;
                    case MsStylesDrawOpKind.Icon:
                        DrawIcon(canvas, op, textures, scale);
                        break;
                    case MsStylesDrawOpKind.Clip:
                        var clip = Logical(op.Destination, scale);
                        canvas.SaveState();
                        canvas.IntersectScissor(clip.X, clip.Y, clip.Width, clip.Height);
                        break;
                    case MsStylesDrawOpKind.Restore:
                        canvas.RestoreState();
                        break;
                }
            }
        }
        finally
        {
            canvas.SetGlobalAlpha(1f);
            canvas.SetAntiAlias(antiAlias);
        }
    }

    private void DrawImage(Canvas canvas, in MsStylesDrawOp op, MsStylesQuillTextures textures, float scale)
    {
        if (op.Bitmap < 0 || op.Bitmap >= Images.Count)
        {
            return;
        }

        var bitmap = Images[op.Bitmap];
        var frame = op.Frame.IsEmpty ? new Rectangle(0, 0, bitmap.Width, bitmap.Height) : op.Frame;
        frame.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        var destination = op.Destination;
        var sourceF = op.Source;
        if (!op.TileX && !op.TileY && !Trim(ref sourceF, ref destination, frame))
        {
            return;
        }

        var source = Texels(sourceF);
        source.Intersect(frame);
        if (source.Width <= 0 || source.Height <= 0)
        {
            return;
        }

        canvas.SetGlobalAlpha(Math.Clamp(op.Opacity, 0f, 1f));
        if (!op.TileX && !op.TileY)
        {
            var scaleX = op.Smooth ? 1 : NearestScale(source.Width, destination.Width);
            var scaleY = op.Smooth ? 1 : NearestScale(source.Height, destination.Height);
            var texture = textures.Piece(bitmap, source, scaleX, scaleY);
            var rect = Logical(destination, scale);
            canvas.DrawImage(texture, rect.X, rect.Y, rect.Width, rect.Height);
            canvas.SetGlobalAlpha(1f);
            return;
        }

        var tileWidth = source.Width * op.TileScale;
        var tileHeight = source.Height * op.TileScale;
        if (tileWidth < 0.5f || tileHeight < 0.5f)
        {
            canvas.SetGlobalAlpha(1f);
            return;
        }

        var tile = textures.Piece(bitmap, source, 1, 1);
        var bounds = Logical(destination, scale);
        canvas.SaveState();
        canvas.IntersectScissor(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        for (var y = destination.Top; y < destination.Bottom; y += tileHeight)
        {
            for (var x = destination.Left; x < destination.Right; x += tileWidth)
            {
                canvas.DrawImage(tile, x / scale, y / scale, tileWidth / scale, tileHeight / scale);
            }
        }

        canvas.RestoreState();
        canvas.SetGlobalAlpha(1f);
    }

    private static void DrawBorder(Canvas canvas, in MsStylesDrawOp op, float scale)
    {
        var rect = Logical(op.Destination, scale);
        var size = op.Size / scale;
        var color = Color(op.Color);
        if (op.Shape is not (BorderType.Ellipse or BorderType.RoundRect))
        {
            canvas.RectFilled(rect.X, rect.Y, rect.Width, size, color);
            canvas.RectFilled(rect.X, rect.Bottom - size, rect.Width, size, color);
            canvas.RectFilled(rect.X, rect.Y + size, size, rect.Height - (2 * size), color);
            canvas.RectFilled(rect.Right - size, rect.Y + size, size, rect.Height - (2 * size), color);
            return;
        }

        var half = size / 2;
        canvas.SetAntiAlias(true);
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(size);
        canvas.BeginPath();
        if (op.Shape == BorderType.Ellipse)
        {
            canvas.Ellipse(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2), (rect.Width / 2) - half, (rect.Height / 2) - half);
        }
        else
        {
            canvas.RoundedRect(rect.X + half, rect.Y + half, rect.Width - size, rect.Height - size, op.Radius / scale);
        }

        canvas.Stroke();
        canvas.SetAntiAlias(false);
    }

    private static void DrawGradient(Canvas canvas, in MsStylesDrawOp op, MsStylesQuillTextures textures, float scale)
    {
        var rect = Logical(op.Destination, scale);
        var texture = textures.Gradient(op.Gradient);
        switch (op.Shape)
        {
            case BorderType.Ellipse:
                canvas.SetAntiAlias(true);
                canvas.SetBrushTexture(texture);
                canvas.SetBrushTextureTransform(Transform2D.CreateTranslation(rect.X, rect.Y) * Transform2D.CreateScale(rect.Width, rect.Height));
                canvas.BeginPath();
                canvas.Ellipse(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2), rect.Width / 2, rect.Height / 2);
                canvas.SetFillColor(new Color32(255, 255, 255, 255));
                canvas.Fill();
                canvas.ClearBrushTexture();
                canvas.SetAntiAlias(false);
                break;
            case BorderType.RoundRect:
                canvas.SetAntiAlias(true);
                canvas.DrawImageRounded(texture, rect.X, rect.Y, rect.Width, rect.Height, op.Radius / scale);
                canvas.SetAntiAlias(false);
                break;
            default:
                canvas.DrawImage(texture, rect.X, rect.Y, rect.Width, rect.Height);
                break;
        }
    }

    private void DrawIcon(Canvas canvas, in MsStylesDrawOp op, MsStylesQuillTextures textures, float scale)
    {
        object? key = null;
        (int Width, int Height, byte[] Rgba)? pixels = null;
        if (op.Icon.Pixels is { } buffer)
        {
            key = buffer;
            pixels = PixelIcon(buffer);
        }

        if (pixels is null)
        {
            var name = op.Icon.Name is { Length: > 0 } named ? named : op.Text;
            if (name is { Length: > 0 } && Icons?.Invoke(name, Math.Max(1, ((int)op.Destination.Width + 15) / 16)) is { } image)
            {
                key = image;
                pixels = NamedIcon(image);
            }
        }

        if (key is null || pixels is not { } icon)
        {
            return;
        }

        var texture = textures.Icon(key, icon.Width, icon.Height, icon.Rgba);
        var rect = Logical(op.Destination, scale);
        canvas.SetGlobalAlpha(Math.Clamp(op.Opacity, 0f, 1f));
        canvas.DrawImage(texture, rect.X, rect.Y, rect.Width, rect.Height);
        canvas.SetGlobalAlpha(1f);
    }

    private (int Width, int Height, byte[] Rgba)? PixelIcon(IBuffer buffer)
    {
        if (_pixelIcons.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        (int, int, byte[])? icon = null;
        if (buffer.Width > 0 && buffer.Height > 0 && buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            try
            {
                var rgba = new byte[buffer.Width * buffer.Height * 4];
                unsafe
                {
                    var data = (byte*)view.Data;
                    for (var y = 0; y < buffer.Height; y++)
                    {
                        var row = data + ((long)y * view.Stride);
                        for (var x = 0; x < buffer.Width; x++)
                        {
                            var i = ((y * buffer.Width) + x) * 4;
                            rgba[i] = row[(x * 4) + 2];
                            rgba[i + 1] = row[(x * 4) + 1];
                            rgba[i + 2] = row[x * 4];
                            rgba[i + 3] = row[(x * 4) + 3];
                        }
                    }
                }

                icon = (buffer.Width, buffer.Height, rgba);
            }
            finally
            {
                buffer.EndDataAccess();
            }
        }

        _pixelIcons[buffer] = icon;
        buffer.Destroyed += () => ForgetIcon(buffer);
        return icon;
    }

    private void ForgetIcon(IBuffer buffer)
    {
        _pixelIcons.Remove(buffer);
        foreach (var textures in _textures)
        {
            textures.Value.Forget(buffer);
        }
    }

    private (int Width, int Height, byte[] Rgba)? NamedIcon(SKImage image)
    {
        if (_namedIcons.TryGetValue(image, out var cached))
        {
            return cached;
        }

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var rgba = new byte[info.BytesSize];
        (int, int, byte[])? icon = null;
        unsafe
        {
            fixed (byte* pixels = rgba)
            {
                if (image.ReadPixels(info, (nint)pixels, info.RowBytes, 0, 0))
                {
                    icon = (image.Width, image.Height, rgba);
                }
            }
        }

        _namedIcons[image] = icon;
        return icon;
    }

    private static bool Trim(ref RectangleF source, ref RectangleF destination, Rectangle frame)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return false;
        }

        var kx = destination.Width / source.Width;
        var ky = destination.Height / source.Height;
        var left = MathF.Max(source.Left, frame.Left);
        var top = MathF.Max(source.Top, frame.Top);
        var right = MathF.Min(source.Right, frame.Right);
        var bottom = MathF.Min(source.Bottom, frame.Bottom);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        destination = RectangleF.FromLTRB(
            destination.Left + ((left - source.Left) * kx),
            destination.Top + ((top - source.Top) * ky),
            destination.Right - ((source.Right - right) * kx),
            destination.Bottom - ((source.Bottom - bottom) * ky));
        source = RectangleF.FromLTRB(left, top, right, bottom);
        return true;
    }

    private static Rectangle Texels(RectangleF source)
    {
        var (left, right) = Span(source.Left, source.Right);
        var (top, bottom) = Span(source.Top, source.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static (int Start, int End) Span(float start, float end)
    {
        var first = (int)MathF.Floor(start + 0.5f);
        var last = (int)MathF.Floor(end + 0.5f);
        if (last > first)
        {
            return (first, last);
        }

        var center = (int)MathF.Floor((start + end) / 2);
        return (center, center + 1);
    }

    private static int NearestScale(int source, float destination)
    {
        if (source <= 1)
        {
            return 1;
        }

        var ratio = destination / source;
        var whole = (int)MathF.Round(ratio);
        return whole is >= 2 and <= MaxNearestScale && MathF.Abs(ratio - whole) < 0.001f ? whole : 1;
    }

    private static RectangleF Logical(RectangleF rect, float scale) =>
        new(rect.X / scale, rect.Y / scale, rect.Width / scale, rect.Height / scale);

    private static Color32 Color(uint argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
