using Pixman;

namespace Basin;

/// <summary>
/// Carries the source of one pixel shader in each dialect that the consumer wrote. A renderer compiles the dialect it reads and ignores the others.
/// </summary>
public readonly record struct PixelShaderSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PixelShaderSource"/> struct with no dialects.
    /// </summary>
    public PixelShaderSource()
    {
    }

    /// <summary>
    /// Gets the GLSL source for the GL renderer. It defines <c>basin_pixel</c>, and basin owns <c>main</c>.
    /// </summary>
    public string? Glsl { get; init; } = null;

    /// <summary>
    /// Gets the SkSL source for the Skia renderers. It defines <c>basin_pixel</c>, and basin owns <c>main</c>.
    /// </summary>
    public string? Sksl { get; init; } = null;

    /// <summary>
    /// Gets the compiled SPIR-V fragment shader for the Vulkan renderer. It owns <c>main</c> and applies <c>u_alpha</c> itself.
    /// </summary>
    public ReadOnlyMemory<byte> SpirV { get; init; } = default;

    /// <summary>
    /// Gets a value indicating whether the shader samples the texture of a draw. Such a shader draws only through <see cref="TextureRenderOptions.Shader"/>.
    /// </summary>
    public bool SamplesTexture { get; init; } = false;
}
