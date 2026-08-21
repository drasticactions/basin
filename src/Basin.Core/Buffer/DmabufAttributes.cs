using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

public struct DmabufAttributes
{
    public int Width;

    public int Height;

    public DrmFormat Format;

    public ulong Modifier;

    public int PlaneCount;

    public ulong SamplingDevice;

    public DmabufPlanes<int> Fds;

    public DmabufPlanes<uint> Offsets;

    public DmabufPlanes<uint> Strides;
}
