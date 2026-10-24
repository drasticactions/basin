namespace Basin.Scene3D.Gltf;

/// <summary>
/// The target of a glTF animation channel.
/// </summary>
public sealed class GltfChannelTarget
{
    /// <summary>
    /// Gets or sets the index of the node, or <see langword="null"/> when the channel drives no node.
    /// </summary>
    public int? Node { get; set; }

    /// <summary>
    /// Gets or sets the property: "translation", "rotation", "scale" or "weights". The avatar loader ignores "weights".
    /// </summary>
    public string? Path { get; set; }
}
