using Pixman;

namespace Basin;

/// <summary>
/// Holds the value of one pixel shader uniform. A uniform with fewer than four components reads only the first ones.
/// </summary>
/// <param name="X">The first component.</param>
/// <param name="Y">The second component.</param>
/// <param name="Z">The third component.</param>
/// <param name="W">The fourth component.</param>
public readonly record struct PixelShaderUniformValue(float X, float Y = 0f, float Z = 0f, float W = 0f)
{
    /// <summary>
    /// Converts a float to a uniform value.
    /// </summary>
    /// <param name="value">The value for a <see cref="PixelShaderUniformType.Float"/> uniform.</param>
    /// <returns>The uniform value.</returns>
    public static implicit operator PixelShaderUniformValue(float value) => new(value);

    /// <summary>
    /// Converts a pair of floats to a uniform value.
    /// </summary>
    /// <param name="value">The value for a <see cref="PixelShaderUniformType.Float2"/> uniform.</param>
    /// <returns>The uniform value.</returns>
    public static implicit operator PixelShaderUniformValue((float X, float Y) value) => new(value.X, value.Y);

    /// <summary>
    /// Converts three floats to a uniform value.
    /// </summary>
    /// <param name="value">The value for a <see cref="PixelShaderUniformType.Float3"/> uniform.</param>
    /// <returns>The uniform value.</returns>
    public static implicit operator PixelShaderUniformValue((float X, float Y, float Z) value) => new(value.X, value.Y, value.Z);

    /// <summary>
    /// Converts four floats to a uniform value.
    /// </summary>
    /// <param name="value">The value for a <see cref="PixelShaderUniformType.Float4"/> uniform.</param>
    /// <returns>The uniform value.</returns>
    public static implicit operator PixelShaderUniformValue((float X, float Y, float Z, float W) value) => new(value.X, value.Y, value.Z, value.W);
}
