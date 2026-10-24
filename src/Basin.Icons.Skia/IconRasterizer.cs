using Basin.Render.Skia;
using SkiaSharp;
using Svg.Skia;

namespace Basin.Icons.Skia;

/// <summary>
/// Draws an icon file into a square of a given pixel size. An SVG file scales as vectors, and any other file decodes as a bitmap.
/// The icon keeps its aspect ratio and is centered in the square.
/// </summary>
public static class IconRasterizer
{
    /// <summary>
    /// Draws the icon at <paramref name="path"/> onto <paramref name="canvas"/> at the canvas origin. The canvas is not cleared first.
    /// </summary>
    /// <param name="path">The path of an SVG, PNG or other image file that Skia can decode.</param>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="sizePx">The side of the square, in pixels.</param>
    /// <returns><see langword="false"/> when the path is empty, the size is not positive, or the file cannot be read or decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="canvas"/> is <see langword="null"/>.</exception>
    public static bool Draw(string path, SKCanvas canvas, int sizePx)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (string.IsNullOrEmpty(path) || sizePx <= 0)
        {
            return false;
        }

        try
        {
            return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? DrawSvg(path, canvas, sizePx)
                : DrawBitmap(path, canvas, sizePx);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Clears a caller-owned BGRA8888 premultiplied pixel buffer to transparent and draws the icon into it.
    /// </summary>
    /// <param name="path">The path of the image file.</param>
    /// <param name="pixels">The address of the first pixel. The caller keeps ownership of the memory.</param>
    /// <param name="rowBytes">The stride of the buffer, in bytes. It must be at least four times <paramref name="sizePx"/>.</param>
    /// <param name="sizePx">The width and height of the buffer, in pixels.</param>
    /// <returns><see langword="false"/> when an argument is not valid or the icon cannot be drawn.</returns>
    public static bool Draw(string path, nint pixels, int rowBytes, int sizePx)
    {
        if (pixels == 0 || sizePx <= 0 || rowBytes < sizePx * 4)
        {
            return false;
        }

        var info = new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, pixels, rowBytes);
        if (surface is null)
        {
            return false;
        }

        surface.Canvas.Clear(SKColors.Transparent);
        var drawn = Draw(path, surface.Canvas, sizePx);
        surface.Canvas.Flush();
        return drawn;
    }

    /// <summary>
    /// Rasterizes the icon into a new square image with a transparent background.
    /// </summary>
    /// <param name="path">The path of the image file.</param>
    /// <param name="sizePx">The side of the image, in pixels.</param>
    /// <returns>A new image that <see cref="SkiaCensus"/> tracks and the caller owns, or <see langword="null"/> when the icon cannot be drawn.</returns>
    public static SKImage? Image(string path, int sizePx)
    {
        if (string.IsNullOrEmpty(path) || sizePx <= 0)
        {
            return null;
        }

        using var surface = SKSurface.Create(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return null;
        }

        surface.Canvas.Clear(SKColors.Transparent);
        if (!Draw(path, surface.Canvas, sizePx))
        {
            return null;
        }

        surface.Canvas.Flush();
        return SkiaCensus.Track(surface.Snapshot());
    }

    /// <summary>
    /// Rasterizes the icon into a new array of straight-alpha ARGB32 pixels, in rows from the top.
    /// </summary>
    /// <param name="path">The path of the image file.</param>
    /// <param name="sizePx">The side of the square, in pixels.</param>
    /// <returns>An array of <paramref name="sizePx"/> squared pixels, or <see langword="null"/> when the icon cannot be drawn.</returns>
    public static unsafe uint[]? Argb(string path, int sizePx)
    {
        if (string.IsNullOrEmpty(path) || sizePx <= 0)
        {
            return null;
        }

        var bitmap = SkiaCensus.Track(new SKBitmap(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul)));
        try
        {
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                if (!Draw(path, canvas, sizePx))
                {
                    return null;
                }

                canvas.Flush();
            }

            var result = new uint[sizePx * sizePx];
            var pixels = (uint*)bitmap.GetPixels();
            for (var i = 0; i < result.Length; i++)
            {
                var premul = pixels[i];
                var alpha = premul >> 24;
                if (alpha == 0)
                {
                    continue;
                }

                result[i] = (alpha << 24)
                    | (Straight(premul, 16, alpha) << 16)
                    | (Straight(premul, 8, alpha) << 8)
                    | Straight(premul, 0, alpha);
            }

            return result;
        }
        finally
        {
            SkiaCensus.Release(bitmap);
        }
    }

    private static uint Straight(uint premul, int shift, uint alpha) =>
        Math.Min(255u, ((premul >> shift) & 0xff) * 255 / alpha);

    private static bool DrawSvg(string path, SKCanvas canvas, int sizePx)
    {
        using var svg = new SKSvg();
        if (svg.Load(path) is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
        {
            return false;
        }

        var bounds = picture.CullRect;
        var scale = Math.Min(sizePx / bounds.Width, sizePx / bounds.Height);
        canvas.Save();
        canvas.Translate((sizePx - (bounds.Width * scale)) / 2f, (sizePx - (bounds.Height * scale)) / 2f);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Restore();
        return true;
    }

    private static bool DrawBitmap(string path, SKCanvas canvas, int sizePx)
    {
        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return false;
        }

        var fit = Math.Min(sizePx / (float)bitmap.Width, sizePx / (float)bitmap.Height);
        var width = bitmap.Width * fit;
        var height = bitmap.Height * fit;
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(
            image,
            new SKRect((sizePx - width) / 2f, (sizePx - height) / 2f, (sizePx + width) / 2f, (sizePx + height) / 2f),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return true;
    }
}
