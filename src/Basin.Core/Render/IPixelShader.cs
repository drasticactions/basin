using Pixman;

namespace Basin;

/// <summary>
/// Represents a pixel shader that <see cref="IRenderer.CompilePixelShader"/> compiled. It lives until it is disposed.
/// </summary>
public interface IPixelShader : IDisposable
{
    /// <summary>
    /// Replaces the value of every declared uniform at once. The next draw uses the new values.
    /// </summary>
    /// <param name="values">One value per declared uniform, in declaration order. The count must match the declaration count.</param>
    void SetUniforms(ReadOnlySpan<PixelShaderUniformValue> values);

    /// <summary>
    /// Creates a shader that shares the compiled program of this shader and holds its own uniform values. The program lives until this shader and every instance are disposed.
    /// </summary>
    /// <returns>The new shader with the current uniform values, or <see langword="null"/> when the renderer cannot share the program.</returns>
    IPixelShader? CreateInstance() => null;
}
