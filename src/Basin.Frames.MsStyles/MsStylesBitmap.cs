namespace Basin.Frames.MsStyles;

/// <summary>
/// A decoded theme image in premultiplied BGRA, owned by an <see cref="MsStylesImageCache"/>. The cache never changes it after it adds it.
/// </summary>
public sealed class MsStylesBitmap
{
    internal MsStylesBitmap(int handle, int width, int height, byte[] pixels, bool hasAlpha, bool hasDefaultKey)
    {
        Handle = handle;
        Width = width;
        Height = height;
        Pixels = pixels;
        HasAlpha = hasAlpha;
        HasDefaultKey = hasDefaultKey;
    }

    /// <summary>
    /// Gets the index of the bitmap in its cache. A draw operation refers to the bitmap by this number.
    /// </summary>
    public int Handle { get; }

    /// <summary>
    /// Gets the width in texels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height in texels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the pixels as premultiplied BGRA rows with no padding, four bytes for each texel. Callers must not change the array.
    /// </summary>
    public byte[] Pixels { get; }

    /// <summary>
    /// Gets a value indicating whether a texel is not fully opaque. A bitmap with alpha ignores a transparency key.
    /// </summary>
    public bool HasAlpha { get; }

    /// <summary>
    /// Gets a value indicating whether an opaque texel is magenta, the default transparency key.
    /// </summary>
    public bool HasDefaultKey { get; }

    /// <summary>
    /// Reads one texel. The method does no bounds check beyond the array's own.
    /// </summary>
    /// <param name="x">The column, from 0 to <see cref="Width"/> minus 1.</param>
    /// <param name="y">The row, from 0 to <see cref="Height"/> minus 1.</param>
    /// <returns>The texel as premultiplied 0xAARRGGBB.</returns>
    public uint PixelAt(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (uint)(Pixels[i] | (Pixels[i + 1] << 8) | (Pixels[i + 2] << 16) | (Pixels[i + 3] << 24));
    }
}
