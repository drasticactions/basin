using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Basin.Icons.Skia;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// Loads application icons for the Avalonia prompts.
/// </summary>
public static class PromptIcon
{
    /// <summary>
    /// Draws an icon file into a new square bitmap with premultiplied BGRA pixels.
    /// </summary>
    /// <param name="path">The path of the icon file.</param>
    /// <param name="sizePx">The edge length of the bitmap in pixels.</param>
    /// <returns>The bitmap, which the caller must dispose. It is <see langword="null"/> for an empty path, a size of 0 or less, or a file that does not draw.</returns>
    public static Bitmap? Load(string path, int sizePx)
    {
        if (string.IsNullOrEmpty(path) || sizePx <= 0)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(new PixelSize(sizePx, sizePx), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        bool drawn;
        using (var frame = bitmap.Lock())
        {
            drawn = IconRasterizer.Draw(path, frame.Address, frame.RowBytes, sizePx);
        }

        if (!drawn)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }
}
