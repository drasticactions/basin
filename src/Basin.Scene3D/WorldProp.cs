using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A mesh with a pose. The consumer writes the pose, for example from a physics body.
/// </summary>
public sealed class WorldProp
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldProp"/> class at the origin with no rotation.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    public WorldProp(WorldMesh mesh)
    {
        Mesh = mesh;
    }

    /// <summary>
    /// Gets the mesh to draw.
    /// </summary>
    public WorldMesh Mesh { get; }

    /// <summary>
    /// Gets or sets the position in world space.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Gets or sets the rotation from model space to world space.
    /// </summary>
    public Quaternion Orientation { get; set; } = Quaternion.Identity;

    /// <summary>
    /// Gets or sets the room that holds the prop. A prop in a room is visible only when the room is visible. A prop in no room is tested on the frustum alone.
    /// </summary>
    public WorldRoom? Room { get; set; }

    /// <summary>
    /// Gets or sets a value that the consumer owns. The library does not read it.
    /// </summary>
    public object? Tag { get; set; }

    /// <summary>
    /// Gets the model matrix: the rotation, then the translation.
    /// </summary>
    public Matrix4x4 Model => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);

    /// <summary>
    /// Gets the world-space box round the posed mesh.
    /// </summary>
    public WorldBounds Bounds => Mesh.Bounds.Transformed(Model);
}
