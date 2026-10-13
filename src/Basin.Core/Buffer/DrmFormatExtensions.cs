namespace Basin;

public static class DrmFormatExtensions
{
    public static bool HasAlpha(this DrmFormat format) => format is
        DrmFormat.Argb8888 or DrmFormat.Abgr8888 or
        DrmFormat.Argb2101010 or DrmFormat.Abgr2101010 or
        DrmFormat.Abgr16161616f;

    public static bool IsOpaque(this DrmFormat format) => format is
        DrmFormat.Xrgb8888 or DrmFormat.Xbgr8888 or
        DrmFormat.Xrgb2101010 or DrmFormat.Xbgr2101010 or
        DrmFormat.Xbgr16161616f or DrmFormat.Rgb565 or
        DrmFormat.Nv12 or DrmFormat.P010;

    public static DrmFormat OpaqueSubstitute(this DrmFormat format) => format switch
    {
        DrmFormat.Argb8888 => DrmFormat.Xrgb8888,
        DrmFormat.Abgr8888 => DrmFormat.Xbgr8888,
        DrmFormat.Argb2101010 => DrmFormat.Xrgb2101010,
        DrmFormat.Abgr2101010 => DrmFormat.Xbgr2101010,
        DrmFormat.Abgr16161616f => DrmFormat.Xbgr16161616f,
        _ => format,
    };

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
        _ => throw new NotSupportedException($"No pixel size known for format 0x{(uint)format:X8}."),
    };

    public static int PlaneHeight(this DrmFormat format, int plane, int height) =>
        plane > 0 && (uint)format is
            0x3231564E or 0x3132564E or
            0x30313050 or 0x32313050 or 0x36313050 or
            0x32315559 or 0x32315659
            ? (height + 1) / 2
            : height;

    public static DrmFormat FromWlShm(uint shmFormat) => shmFormat switch
    {
        0 => DrmFormat.Argb8888,
        1 => DrmFormat.Xrgb8888,
        _ => (DrmFormat)shmFormat,
    };

    public static uint ToWlShm(this DrmFormat format) => format switch
    {
        DrmFormat.Argb8888 => 0,
        DrmFormat.Xrgb8888 => 1,
        _ => (uint)format,
    };
}
