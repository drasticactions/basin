namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF animation sampler: keyframe times and the values at them.
/// </summary>
public sealed class GltfAnimationSampler
{
    /// <summary>
    /// Gets or sets the index of the accessor that holds the keyframe times in seconds.
    /// </summary>
    public int Input { get; set; }

    /// <summary>
    /// Gets or sets the index of the accessor that holds the keyframe values.
    /// </summary>
    public int Output { get; set; }

    /// <summary>
    /// Gets or sets the interpolation: "LINEAR", "STEP" or "CUBICSPLINE". <see langword="null"/> means linear.
    /// </summary>
    public string? Interpolation { get; set; }
}
