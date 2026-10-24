namespace Basin;

/// <summary>
/// Layout facts for <see cref="DrmFormat"/> values and conversions to and from <c>wl_shm</c> codes.
/// </summary>
public static class DrmFormatExtensions
{
    /// <summary>
    /// Tests whether the format carries an alpha channel.
    /// </summary>
    /// <param name="format">The format to test.</param>
    /// <returns><see langword="true"/> for the RGB formats with alpha. It is <see langword="false"/> for unknown formats.</returns>
    public static bool HasAlpha(this DrmFormat format) => format is
        DrmFormat.Argb8888 or DrmFormat.Abgr8888 or
        DrmFormat.Argb2101010 or DrmFormat.Abgr2101010 or
        DrmFormat.Abgr16161616f;

    /// <summary>
    /// Tests whether the format is known to be opaque. A format can be neither opaque nor alpha when basin does not know it.
    /// </summary>
    /// <param name="format">The format to test.</param>
    /// <returns><see langword="true"/> for the X formats, RGB565, NV12 and P010.</returns>
    public static bool IsOpaque(this DrmFormat format) => format is
        DrmFormat.Xrgb8888 or DrmFormat.Xbgr8888 or
        DrmFormat.Xrgb2101010 or DrmFormat.Xbgr2101010 or
        DrmFormat.Xbgr16161616f or DrmFormat.Rgb565 or
        DrmFormat.Nv12 or DrmFormat.P010;

    /// <summary>
    /// Gets the format with the alpha channel replaced by an unused channel.
    /// </summary>
    /// <param name="format">The format to convert.</param>
    /// <returns>The X variant of an alpha format, or <paramref name="format"/> unchanged.</returns>
    public static DrmFormat OpaqueSubstitute(this DrmFormat format) => format switch
    {
        DrmFormat.Argb8888 => DrmFormat.Xrgb8888,
        DrmFormat.Abgr8888 => DrmFormat.Xbgr8888,
        DrmFormat.Argb2101010 => DrmFormat.Xrgb2101010,
        DrmFormat.Abgr2101010 => DrmFormat.Xbgr2101010,
        DrmFormat.Abgr16161616f => DrmFormat.Xbgr16161616f,
        _ => format,
    };

    /// <summary>
    /// Gets the size of one pixel for a single-plane format.
    /// </summary>
    /// <param name="format">The format to measure.</param>
    /// <returns>The pixel size in bytes.</returns>
    /// <exception cref="NotSupportedException">The format is unknown or has more than one plane.</exception>
    public static int BytesPerPixel(this DrmFormat format) => format switch
    {
        DrmFormat.Xrgb8888 or DrmFormat.Argb8888 or
        DrmFormat.Xbgr8888 or DrmFormat.Abgr8888 or
        DrmFormat.Xrgb2101010 or DrmFormat.Argb2101010 or
        DrmFormat.Xbgr2101010 or DrmFormat.Abgr2101010 => 4,
        DrmFormat.Xbgr16161616f or DrmFormat.Abgr16161616f => 8,
        DrmFormat.Rgb565 => 2,
        DrmFormat.R8 => 1,
        DrmFormat.Gr88 => 2,
        _ => throw new NotSupportedException($"The format 0x{(uint)format:X8} has no known pixel size."),
    };

    /// <summary>
    /// Gets the row count of one plane. Chroma planes of the 4:2:0 YUV formats have half the height, rounded up.
    /// </summary>
    /// <param name="format">The format of the buffer.</param>
    /// <param name="plane">The zero-based plane index.</param>
    /// <param name="height">The buffer height in pixels.</param>
    /// <returns>The number of rows in <paramref name="plane"/>.</returns>
    public static int PlaneHeight(this DrmFormat format, int plane, int height) =>
        plane > 0 && (uint)format is
            0x3231564E or 0x3132564E or
            0x30313050 or 0x32313050 or 0x36313050 or
            0x32315559 or 0x32315659
            ? (height + 1) / 2
            : height;

    /// <summary>
    /// Converts a <c>wl_shm</c> format code to a DRM format. Codes 0 and 1 are ARGB8888 and XRGB8888. Other codes are fourcc values already.
    /// </summary>
    /// <param name="shmFormat">The <c>wl_shm</c> format code.</param>
    /// <returns>The matching DRM format.</returns>
    public static DrmFormat FromWlShm(uint shmFormat) => shmFormat switch
    {
        0 => DrmFormat.Argb8888,
        1 => DrmFormat.Xrgb8888,
        _ => (DrmFormat)shmFormat,
    };

    /// <summary>
    /// Converts a DRM format to a <c>wl_shm</c> format code. ARGB8888 and XRGB8888 become 0 and 1.
    /// </summary>
    /// <param name="format">The DRM format.</param>
    /// <returns>The matching <c>wl_shm</c> code.</returns>
    public static uint ToWlShm(this DrmFormat format) => format switch
    {
        DrmFormat.Argb8888 => 0,
        DrmFormat.Xrgb8888 => 1,
        _ => (uint)format,
    };
}
