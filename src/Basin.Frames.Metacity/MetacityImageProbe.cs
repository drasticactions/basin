using Basin.Freedesktop;
using SkiaSharp;
using Svg.Skia;

namespace Basin.Frames.Metacity;

internal static class MetacityImageProbe
{
    public const int ThemeIconSize = 64;

    public static MetacityImageInfo? Probe(string directory, string filename, out string? error)
    {
        error = null;
        string path;
        if (filename.StartsWith("theme:", StringComparison.Ordinal))
        {
            var search = new IconSearch { Sizes = [ThemeIconSize], ReadDesktopEntry = false };
            if (search.Find(filename[6..]) is not { } found)
            {
                error = $"Icon '{filename[6..]}' not present in theme";
                return null;
            }

            path = found;
        }
        else
        {
            path = System.IO.Path.Combine(directory, filename);
            if (!File.Exists(path))
            {
                error = $"Failed to open file '{path}': No such file or directory";
                return null;
            }
        }

        var isSvg = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase);
        using var bitmap = isSvg ? RasterizeSvg(path) : SKBitmap.Decode(path);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            error = $"Failed to load image '{path}'";
            return null;
        }

        var (horizontal, vertical) = Stripes(bitmap);
        return new MetacityImageInfo
        {
            Filename = filename,
            Path = path,
            Width = bitmap.Width,
            Height = bitmap.Height,
            HorizontalStripes = horizontal,
            VerticalStripes = vertical,
            IsSvg = isSvg,
        };
    }

    public static SKBitmap? RasterizeSvg(string path, int width = 0, int height = 0)
    {
        using var svg = new SKSvg();
        if (svg.Load(path) is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
        {
            return null;
        }

        var bounds = picture.CullRect;
        if (width <= 0)
        {
            width = (int)Math.Ceiling(bounds.Width);
        }

        if (height <= 0)
        {
            height = (int)Math.Ceiling(bounds.Height);
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(width / bounds.Width, height / bounds.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();
        return bitmap;
    }

    private static unsafe (bool Horizontal, bool Vertical) Stripes(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var bytesPerPixel = bitmap.BytesPerPixel;
        var stride = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();

        var horizontal = true;
        for (var y = 0; y < height && horizontal; y++)
        {
            var row = pixels + y * stride;
            for (var x = 1; x < width; x++)
            {
                if (new ReadOnlySpan<byte>(row, bytesPerPixel).SequenceEqual(new ReadOnlySpan<byte>(row + x * bytesPerPixel, bytesPerPixel)))
                {
                    continue;
                }

                horizontal = false;
                break;
            }
        }

        var vertical = true;
        var first = new ReadOnlySpan<byte>(pixels, width * bytesPerPixel);
        for (var y = 1; y < height; y++)
        {
            if (!first.SequenceEqual(new ReadOnlySpan<byte>(pixels + y * stride, width * bytesPerPixel)))
            {
                vertical = false;
                break;
            }
        }

        return (horizontal, vertical);
    }
}
