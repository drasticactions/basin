namespace Basin;

/// <summary>
/// Identifies the color space in which a renderer blends translucent content.
/// </summary>
public enum RenderBlendSpace
{
    /// <summary>
    /// The renderer blends display-encoded values, as KMS planes do.
    /// </summary>
    Encoded,

    /// <summary>
    /// The renderer blends in linear light. A translucent buffer then looks different on a hardware plane.
    /// </summary>
    Linear,
}
