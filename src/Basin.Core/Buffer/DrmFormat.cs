namespace Basin;

public enum DrmFormat : uint
{
    Invalid = 0,

    Xrgb8888 = 0x34325258,

    Argb8888 = 0x34325241,

    Xbgr8888 = 0x34324258,

    Abgr8888 = 0x34324241,

    Xrgb2101010 = 0x30335258,

    Argb2101010 = 0x30335241,

    Xbgr2101010 = 0x30334258,

    Abgr2101010 = 0x30334241,

    Xbgr16161616f = 0x48344258,

    Abgr16161616f = 0x48344241,

    Rgb565 = 0x36314752,

    Nv12 = 0x3231564E,

    P010 = 0x30313050,
}
