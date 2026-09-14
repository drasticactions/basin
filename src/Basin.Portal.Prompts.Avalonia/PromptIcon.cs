using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace Basin.Portal.Prompts.Avalonia;

public static class PromptIcon
{
    public static Bitmap? Load(string path, int sizePx)
    {
        if (string.IsNullOrEmpty(path) || sizePx <= 0)
        {
            return null;
        }

        try
        {
            return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? FromSvg(path, sizePx) : new Bitmap(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static Bitmap? FromSvg(string path, int sizePx)
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
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
        {
            return null;
        }

        using var stream = encoded.AsStream();
        return new Bitmap(stream);
    }
}
