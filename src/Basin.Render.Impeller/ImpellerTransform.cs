using System.Runtime.InteropServices;
using NImpeller;

namespace Basin.Render.Impeller;

internal static unsafe partial class ImpellerTransform
{
    [LibraryImport("impeller", EntryPoint = "ImpellerDisplayListBuilderTransform")]
    public static partial void Apply(IntPtr builder, ImpellerMatrix* matrix);

    public static ImpellerMatrix ToMatrix(in RenderTransform transform) => new()
    {
        Matrix = new System.Numerics.Matrix4x4(
            (float)transform.M11, (float)transform.M21, 0f, (float)transform.M31,
            (float)transform.M12, (float)transform.M22, 0f, (float)transform.M32,
            0f, 0f, 1f, 0f,
            (float)transform.M13, (float)transform.M23, 0f, (float)transform.M33),
    };
}
