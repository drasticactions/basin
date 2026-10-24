namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF node: a transform with an optional mesh, skin and children.
/// </summary>
public sealed class GltfNode
{
    /// <summary>
    /// Gets or sets the node name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the index of the mesh, or <see langword="null"/> for a node with no mesh.
    /// </summary>
    public int? Mesh { get; set; }

    /// <summary>
    /// Gets or sets the index of the skin, or <see langword="null"/> for a rigid mesh.
    /// </summary>
    public int? Skin { get; set; }

    /// <summary>
    /// Gets or sets the indices of the child nodes.
    /// </summary>
    public int[]? Children { get; set; }

    /// <summary>
    /// Gets or sets the translation as x, y and z, or <see langword="null"/> for none.
    /// </summary>
    public float[]? Translation { get; set; }

    /// <summary>
    /// Gets or sets the rotation quaternion as x, y, z and w, or <see langword="null"/> for none.
    /// </summary>
    public float[]? Rotation { get; set; }

    /// <summary>
    /// Gets or sets the scale as x, y and z, or <see langword="null"/> for a scale of 1.
    /// </summary>
    public float[]? Scale { get; set; }

    /// <summary>
    /// Gets or sets the local matrix as 16 floats in column-major order. When it is present it replaces the translation, rotation and scale.
    /// </summary>
    public float[]? Matrix { get; set; }
}
