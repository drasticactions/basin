namespace Basin;

/// <summary>
/// The layout facts and GL upload parameters for one shm pixel format.
/// </summary>
/// <param name="Format">The DRM format.</param>
/// <param name="BytesPerPixel">The size of one pixel in bytes.</param>
/// <param name="HasAlpha">A value indicating whether the format carries alpha.</param>
/// <param name="OpaqueSubstitute">The format with the alpha channel ignored.</param>
/// <param name="WlShmFormat">The <c>wl_shm</c> format code.</param>
/// <param name="GlInternalFormat">The GL internal format for a texture upload.</param>
/// <param name="GlFormat">The GL pixel format for a texture upload.</param>
/// <param name="GlType">The GL component type for a texture upload.</param>
public readonly record struct PixelFormatInfo(
    DrmFormat Format,
    int BytesPerPixel,
    bool HasAlpha,
    DrmFormat OpaqueSubstitute,
    uint WlShmFormat,
    uint GlInternalFormat,
    uint GlFormat,
    uint GlType)
{
    private const uint GlRgba8 = 0x8058;
    private const uint GlBgraExt = 0x80E1;
    private const uint GlUnsignedByte = 0x1401;

    /// <summary>The facts for ARGB8888.</summary>
    public static readonly PixelFormatInfo Argb8888 = new(
        DrmFormat.Argb8888, 4, true, DrmFormat.Xrgb8888, 0, GlRgba8, GlBgraExt, GlUnsignedByte);

    /// <summary>The facts for XRGB8888.</summary>
    public static readonly PixelFormatInfo Xrgb8888 = new(
        DrmFormat.Xrgb8888, 4, false, DrmFormat.Xrgb8888, 1, GlRgba8, GlBgraExt, GlUnsignedByte);

    /// <summary>
    /// Looks up the facts for a format. Only ARGB8888 and XRGB8888 are known.
    /// </summary>
    /// <param name="format">The format to look up.</param>
    /// <param name="info">The facts, or the default value when the format is unknown.</param>
    /// <returns><see langword="true"/> when the format is known.</returns>
    public static bool TryGet(DrmFormat format, out PixelFormatInfo info)
    {
        switch (format)
        {
            case DrmFormat.Argb8888:
                info = Argb8888;
                return true;
            case DrmFormat.Xrgb8888:
                info = Xrgb8888;
                return true;
            default:
                info = default;
                return false;
        }
    }
}
