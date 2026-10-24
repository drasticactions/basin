using System.Numerics;
using System.Runtime.InteropServices;

namespace Basin.Scene3D;

/// <summary>
/// A vertex of a <see cref="WorldMesh"/>. The layout is sequential, so a pass uploads an array of them as it is.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct WorldVertex
{
    /// <summary>
    /// The position in the mesh's model space, in meters.
    /// </summary>
    public Vector3 Position;
    /// <summary>
    /// The unit normal in model space.
    /// </summary>
    public Vector3 Normal;
    /// <summary>
    /// The texture coordinate into the mesh's <see cref="WorldMesh.Image"/>.
    /// </summary>
    public Vector2 Uv;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldVertex"/> struct.
    /// </summary>
    /// <param name="position">The position in model space.</param>
    /// <param name="normal">The unit normal.</param>
    /// <param name="uv">The texture coordinate.</param>
    public WorldVertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        Uv = uv;
    }
}
