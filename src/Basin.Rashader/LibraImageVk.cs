using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal struct LibraImageVk
{
    public ulong Handle;
    public uint Format;
    public uint Width;
    public uint Height;
}
