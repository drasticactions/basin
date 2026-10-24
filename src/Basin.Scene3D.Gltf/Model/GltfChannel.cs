namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF animation channel: one sampler that drives one property of one node.
/// </summary>
public sealed class GltfChannel
{
    /// <summary>
    /// Gets or sets the index of the sampler in the animation.
    /// </summary>
    public int Sampler { get; set; }

    /// <summary>
    /// Gets or sets the node and property that the channel drives.
    /// </summary>
    public GltfChannelTarget? Target { get; set; }
}
