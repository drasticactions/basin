using System.Runtime.InteropServices;
using NImpeller;

namespace Basin.Render.Impeller;

internal static unsafe partial class ImpellerColorFilters
{
    [LibraryImport("impeller", EntryPoint = "ImpellerColorFilterCreateBlendNew")]
    public static partial IntPtr CreateBlend(ImpellerColor* color, ImpellerBlendMode mode);

    [LibraryImport("impeller", EntryPoint = "ImpellerColorFilterRelease")]
    public static partial void Release(IntPtr filter);

    [LibraryImport("impeller", EntryPoint = "ImpellerPaintSetColorFilter")]
    public static partial void SetOnPaint(IntPtr paint, IntPtr filter);
}
