using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

/// <summary>
/// Describes a dmabuf: its size, format, modifier and up to four planes.
/// </summary>
public struct DmabufAttributes
{
    /// <summary>The width in physical pixels.</summary>
    public int Width;

    /// <summary>The height in physical pixels.</summary>
    public int Height;

    /// <summary>The pixel format.</summary>
    public DrmFormat Format;

    /// <summary>The format modifier that applies to every plane.</summary>
    public ulong Modifier;

    /// <summary>The number of valid entries in <see cref="Fds"/>, <see cref="Offsets"/> and <see cref="Strides"/>.</summary>
    public int PlaneCount;

    /// <summary>The <c>dev_t</c> that the client asked to sample the buffer on, or zero when it named none.</summary>
    public ulong SamplingDevice;

    /// <summary>The file descriptor of each plane.</summary>
    public DmabufPlanes<int> Fds;

    /// <summary>The byte offset of each plane in its file.</summary>
    public DmabufPlanes<uint> Offsets;

    /// <summary>The row stride of each plane in bytes.</summary>
    public DmabufPlanes<uint> Strides;
}
