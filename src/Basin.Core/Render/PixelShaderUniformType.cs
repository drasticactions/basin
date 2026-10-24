using Pixman;

namespace Basin;

/// <summary>
/// Identifies the type of a pixel shader uniform.
/// </summary>
public enum PixelShaderUniformType
{
    /// <summary>
    /// One float.
    /// </summary>
    Float,

    /// <summary>
    /// A vector of two floats.
    /// </summary>
    Float2,

    /// <summary>
    /// A vector of three floats.
    /// </summary>
    Float3,

    /// <summary>
    /// A vector of four floats.
    /// </summary>
    Float4,
}
