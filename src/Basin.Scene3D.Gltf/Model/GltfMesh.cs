namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF mesh: a list of primitives.
/// </summary>
public sealed class GltfMesh
{
    /// <summary>
    /// Gets or sets the mesh name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the primitives, or <see langword="null"/> when the mesh has none.
    /// </summary>
    public GltfPrimitive[]? Primitives { get; set; }
}
