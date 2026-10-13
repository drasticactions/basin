using System.Numerics;
using System.Runtime.InteropServices;

namespace Basin.Effects;

[StructLayout(LayoutKind.Sequential)]
internal struct SmolPassParameters
{
    public Vector4 Source;
    public Vector4 SourcePad;
    public Vector4 Destination;
    public Vector4 Axis;
    public Vector4 Clamp;
}
