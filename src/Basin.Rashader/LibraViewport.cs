using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal struct LibraViewport
{
    public float X;
    public float Y;
    public uint Width;
    public uint Height;
}
