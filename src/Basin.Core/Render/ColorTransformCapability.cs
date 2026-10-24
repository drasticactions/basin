using Pixman;

namespace Basin;

/// <summary>
/// Tells how a renderer takes a color conversion.
/// </summary>
public enum ColorTransformCapability
{
    /// <summary>
    /// The renderer does not convert colors. It composites without a transform.
    /// </summary>
    None,

    /// <summary>
    /// The renderer takes a <see cref="ColorLut3D"/> through <see cref="IRenderer.ImportLut"/> and applies it with straight alpha and trilinear interpolation.
    /// </summary>
    Lut3D,

    /// <summary>
    /// The renderer takes image descriptions for each source and for the output, decodes to linear light, blends there and encodes once.
    /// </summary>
    Decomposed,
}
