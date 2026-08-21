namespace Basin;

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

    public static readonly PixelFormatInfo Argb8888 = new(
        DrmFormat.Argb8888, 4, true, DrmFormat.Xrgb8888, 0, GlRgba8, GlBgraExt, GlUnsignedByte);

    public static readonly PixelFormatInfo Xrgb8888 = new(
        DrmFormat.Xrgb8888, 4, false, DrmFormat.Xrgb8888, 1, GlRgba8, GlBgraExt, GlUnsignedByte);

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
