namespace Basin;

/// <summary>
/// DRM fourcc pixel formats. Each value is the little-endian fourcc code from <c>drm_fourcc.h</c>.
/// </summary>
public enum DrmFormat : uint
{
    /// <summary>No format.</summary>
    Invalid = 0,

    /// <summary>32-bit RGB with 8 bits per channel and an unused byte.</summary>
    Xrgb8888 = 0x34325258,

    /// <summary>32-bit RGB with 8 bits per channel and 8-bit alpha.</summary>
    Argb8888 = 0x34325241,

    /// <summary>32-bit BGR with 8 bits per channel and an unused byte.</summary>
    Xbgr8888 = 0x34324258,

    /// <summary>32-bit BGR with 8 bits per channel and 8-bit alpha.</summary>
    Abgr8888 = 0x34324241,

    /// <summary>32-bit RGB with 10 bits per channel and 2 unused bits.</summary>
    Xrgb2101010 = 0x30335258,

    /// <summary>32-bit RGB with 10 bits per channel and 2-bit alpha.</summary>
    Argb2101010 = 0x30335241,

    /// <summary>32-bit BGR with 10 bits per channel and 2 unused bits.</summary>
    Xbgr2101010 = 0x30334258,

    /// <summary>32-bit BGR with 10 bits per channel and 2-bit alpha.</summary>
    Abgr2101010 = 0x30334241,

    /// <summary>64-bit BGR with 16-bit half floats per channel and an unused channel.</summary>
    Xbgr16161616f = 0x48344258,

    /// <summary>64-bit BGR with 16-bit half floats per channel and half-float alpha.</summary>
    Abgr16161616f = 0x48344241,

    /// <summary>16-bit RGB with 5, 6 and 5 bits per channel.</summary>
    Rgb565 = 0x36314752,

    /// <summary>8-bit YUV 4:2:0 with a Y plane and an interleaved CbCr plane.</summary>
    Nv12 = 0x3231564E,

    /// <summary>10-bit YUV 4:2:0 in 16-bit samples with a Y plane and an interleaved CbCr plane.</summary>
    P010 = 0x30313050,

    /// <summary>One 8-bit channel.</summary>
    R8 = 0x20203852,

    /// <summary>Two 8-bit channels.</summary>
    Gr88 = 0x38385247,
}
