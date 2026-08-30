using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LibraPresetParam
{
    public byte* Name;
    public byte* Description;
    public float Initial;
    public float Minimum;
    public float Maximum;
    public float Step;
}
