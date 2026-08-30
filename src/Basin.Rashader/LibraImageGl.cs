using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal struct LibraImageGl
{
    public uint Handle;
    public uint Format;
    public uint Width;
    public uint Height;
}
