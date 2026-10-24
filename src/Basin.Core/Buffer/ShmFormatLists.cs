namespace Basin;

/// <summary>
/// Standard lists of shm texture formats that a renderer can import.
/// </summary>
public static class ShmFormatLists
{
    /// <summary>Gets ARGB8888 and XRGB8888, the two formats that every <c>wl_shm</c> implementation must support.</summary>
    public static IReadOnlyList<DrmFormat> Basic { get; } = [DrmFormat.Argb8888, DrmFormat.Xrgb8888];

    /// <summary>Gets the 8-bit, 10-bit and RGB565 integer formats.</summary>
    public static IReadOnlyList<DrmFormat> Integer { get; } =
    [
        DrmFormat.Argb8888, DrmFormat.Xrgb8888, DrmFormat.Abgr8888, DrmFormat.Xbgr8888,
        DrmFormat.Argb2101010, DrmFormat.Xrgb2101010, DrmFormat.Abgr2101010, DrmFormat.Xbgr2101010,
        DrmFormat.Rgb565,
    ];

    /// <summary>Gets the <see cref="Integer"/> formats plus the 16-bit half-float formats.</summary>
    public static IReadOnlyList<DrmFormat> All { get; } =
    [
        .. Integer, DrmFormat.Abgr16161616f, DrmFormat.Xbgr16161616f,
    ];
}
