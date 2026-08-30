using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal struct LibraFilterChainGlOptions
{
    public nuint Version;
    public ushort GlslVersion;
    public byte UseDsa;
    public byte ForceNoMipmaps;
    public byte DisableCache;
}
