using SkiaSharp;

namespace Basin.WindowManager.Skia;

/// <summary>
/// Wraps the buffer of a <see cref="DecorationSurface"/> in a Skia surface, for a painter that draws with Skia.
/// </summary>
public static class DecorationCanvas
{
    /// <summary>
    /// Creates a premultiplied BGRA Skia surface over prepared pixels, sized to the last buffer that the decoration prepared.
    /// </summary>
    /// <param name="surface">The decoration whose buffer size to use.</param>
    /// <param name="pixels">The address that <see cref="DecorationSurface.Prepare"/> returned.</param>
    /// <returns>The Skia surface, which the caller disposes before the next prepare, or <see langword="null"/> when Skia cannot create it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public static SKSurface? Create(DecorationSurface surface, nint pixels)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var info = new SKImageInfo(surface.BufferWidth, surface.BufferHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        return SKSurface.Create(info, pixels, surface.BufferWidth * 4);
    }
}
