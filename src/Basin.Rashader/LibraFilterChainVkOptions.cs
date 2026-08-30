using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal struct LibraFilterChainVkOptions
{
    public nuint Version;
    public uint FramesInFlight;
    public byte ForceNoMipmaps;
    public byte UseDynamicRendering;
    public byte DisableCache;
}
