namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF skin: the joints that deform a mesh.
/// </summary>
public sealed class GltfSkin
{
    /// <summary>
    /// Gets or sets the node index of each joint.
    /// </summary>
    public int[]? Joints { get; set; }

    /// <summary>
    /// Gets or sets the index of the accessor that holds one inverse bind matrix for each joint, or <see langword="null"/> for identity matrices.
    /// </summary>
    public int? InverseBindMatrices { get; set; }
}
