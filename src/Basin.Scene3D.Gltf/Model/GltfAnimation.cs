namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF animation: channels that drive node properties through samplers.
/// </summary>
public sealed class GltfAnimation
{
    /// <summary>
    /// Gets or sets the animation name. The avatar loader skips an animation with no name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the channels, or <see langword="null"/> when the file has none.
    /// </summary>
    public GltfChannel[]? Channels { get; set; }

    /// <summary>
    /// Gets or sets the samplers that the channels refer to by index.
    /// </summary>
    public GltfAnimationSampler[]? Samplers { get; set; }
}
