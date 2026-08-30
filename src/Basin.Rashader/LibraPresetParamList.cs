using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LibraPresetParamList
{
    public LibraPresetParam* Parameters;
    public ulong Length;
}
