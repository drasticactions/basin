namespace Basin;

public static class ShmFormatLists
{
    public static IReadOnlyList<DrmFormat> Basic { get; } = [DrmFormat.Argb8888, DrmFormat.Xrgb8888];

    public static IReadOnlyList<DrmFormat> Integer { get; } =
    [
        DrmFormat.Argb8888, DrmFormat.Xrgb8888, DrmFormat.Abgr8888, DrmFormat.Xbgr8888,
        DrmFormat.Argb2101010, DrmFormat.Xrgb2101010, DrmFormat.Abgr2101010, DrmFormat.Xbgr2101010,
        DrmFormat.Rgb565,
    ];

    public static IReadOnlyList<DrmFormat> All { get; } =
    [
        .. Integer, DrmFormat.Abgr16161616f, DrmFormat.Xbgr16161616f,
    ];
}
