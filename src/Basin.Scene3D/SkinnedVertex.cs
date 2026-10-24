using System.Numerics;
using System.Runtime.InteropServices;

namespace Basin.Scene3D;

/// <summary>
/// A vertex of a <see cref="SkinnedMesh"/>. The layout is sequential, so a pass uploads an array of them as it is.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SkinnedVertex
{
    /// <summary>
    /// The position in the bind pose.
    /// </summary>
    public Vector3 Position;
    /// <summary>
    /// The unit normal in the bind pose.
    /// </summary>
    public Vector3 Normal;
    /// <summary>
    /// The texture coordinate.
    /// </summary>
    public Vector2 Uv;
    /// <summary>
    /// Up to four joint indices into <see cref="Skeleton.Joints"/>, stored as floats.
    /// </summary>
    public Vector4 Joints;
    /// <summary>
    /// The weight of each of the four joints. The weights add up to 1.
    /// </summary>
    public Vector4 Weights;
}
