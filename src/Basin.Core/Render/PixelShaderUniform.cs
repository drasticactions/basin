using Pixman;

namespace Basin;

/// <summary>
/// Declares one uniform of a pixel shader.
/// </summary>
/// <param name="Name">The uniform name in the source. Names that start with <c>u_</c> are reserved for basin.</param>
/// <param name="Type">The type of the uniform.</param>
public readonly record struct PixelShaderUniform(string Name, PixelShaderUniformType Type);
