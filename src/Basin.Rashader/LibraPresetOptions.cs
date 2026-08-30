using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal struct LibraPresetOptions
{
    public nuint Version;
    public byte OriginalAspectUniforms;
    public byte FrametimeUniforms;
    public byte SensorUniforms;
}
