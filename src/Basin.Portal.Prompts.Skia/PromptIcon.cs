using SkiaSharp;
using Svg.Skia;

namespace Basin.Portal.Prompts.Skia;

public static class PromptIcon
{
    public static SKImage? Rasterize(string path, int sizePx)
    {
        if (string.IsNullOrEmpty(path) || sizePx <= 0)
        {
            return null;
        }

        try
        {
            return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? FromSvg(path, sizePx) : FromBitmap(path, sizePx);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static SKImage? FromSvg(string path, int sizePx)
    {
        using var svg = new SKSvg();
        if (svg.Load(path) is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
        {
            return null;
        }

        using var surface = SKSurface.Create(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return null;
        }

        var bounds = picture.CullRect;
        var scale = Math.Min(sizePx / bounds.Width, sizePx / bounds.Height);
        surface.Canvas.Translate((sizePx - (bounds.Width * scale)) / 2f, (sizePx - (bounds.Height * scale)) / 2f);
        surface.Canvas.Scale(scale);
        surface.Canvas.Translate(-bounds.Left, -bounds.Top);
        surface.Canvas.DrawPicture(picture);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    private static SKImage? FromBitmap(string path, int sizePx)
    {
        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return null;
        }

        using var surface = SKSurface.Create(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return null;
        }

        var fit = Math.Min(sizePx / (float)bitmap.Width, sizePx / (float)bitmap.Height);
        var width = bitmap.Width * fit;
        var height = bitmap.Height * fit;
        using var image = SKImage.FromBitmap(bitmap);
        surface.Canvas.DrawImage(
            image,
            new SKRect((sizePx - width) / 2f, (sizePx - height) / 2f, (sizePx + width) / 2f, (sizePx + height) / 2f),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        surface.Canvas.Flush();
        return surface.Snapshot();
    }
}
