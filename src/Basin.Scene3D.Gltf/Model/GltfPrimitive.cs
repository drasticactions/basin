namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF mesh primitive.
/// </summary>
public sealed class GltfPrimitive
{
    /// <summary>
    /// Gets or sets the accessor index of each vertex attribute, keyed by name, for example "POSITION" or "NORMAL".
    /// </summary>
    public Dictionary<string, int>? Attributes { get; set; }

    /// <summary>
    /// Gets or sets the index of the accessor that holds the vertex indices, or <see langword="null"/> for a list of vertices in order.
    /// </summary>
    public int? Indices { get; set; }

    /// <summary>
    /// Gets or sets the topology. Only 4, a triangle list, is read. <see langword="null"/> means 4.
    /// </summary>
    public int? Mode { get; set; }
}
