namespace Basin.Capabilities;

/// <summary>
/// Parses and builds image descriptions and bakes conversion tables between them. Without it, ICC descriptions are refused and parametric ones still work.
/// </summary>
public interface IColorProfileService
{
    /// <summary>
    /// Gets the features that the implementation supports now. The color-management protocol advertises only these.
    /// </summary>
    ColorFeatures Features { get; }

    /// <summary>
    /// Parses an ICC profile into an image description.
    /// </summary>
    /// <param name="profile">The bytes of the ICC profile.</param>
    /// <param name="description">The parsed description. On failure it holds a fallback that the caller must not use.</param>
    /// <returns><see langword="true"/> when the profile is valid and supported, otherwise <see langword="false"/>.</returns>
    bool TryParseIcc(ReadOnlySpan<byte> profile, out ImageDescription description);

    /// <summary>
    /// Validates a parametric description that a client assembled.
    /// </summary>
    /// <param name="parameters">The description that holds the parameters the client set.</param>
    /// <param name="description">The accepted description. It can be <paramref name="parameters"/> itself.</param>
    /// <returns><see langword="true"/> when the parameters name both primaries and a transfer function and the implementation supports them, otherwise <see langword="false"/>.</returns>
    bool TryBuildParametric(ImageDescription parameters, out ImageDescription description);

    /// <summary>
    /// Bakes a conversion table from one description to another and imports it into a renderer.
    /// </summary>
    /// <param name="source">The description of the content.</param>
    /// <param name="output">The description of the space to convert into.</param>
    /// <param name="renderer">The renderer that imports the table.</param>
    /// <param name="intent">The render intent of the conversion.</param>
    /// <returns>A table that the caller owns and must dispose, or <see langword="null"/> when no conversion is necessary or the renderer cannot convert.</returns>
    IColorLut? BuildTransform(
        ImageDescription source,
        ImageDescription output,
        IRenderer renderer,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual);

    /// <summary>
    /// Bakes a conversion table from one description to another without a renderer.
    /// </summary>
    /// <param name="source">The description of the content.</param>
    /// <param name="output">The description of the space to convert into.</param>
    /// <param name="intent">The render intent of the conversion.</param>
    /// <returns>The baked table, or <see langword="null"/> when no conversion is necessary or the implementation cannot bake. The default implementation returns <see langword="null"/>.</returns>
    ColorLut3D? BuildLut(
        ImageDescription source,
        ImageDescription output,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual) => null;

    /// <summary>
    /// Gets the description of the color space that an output displays.
    /// </summary>
    /// <param name="output">The output to describe.</param>
    /// <param name="description">The description of the output, or a fallback when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the implementation knows the output, otherwise <see langword="false"/>.</returns>
    bool TryDescribeOutput(IOutput output, out ImageDescription description);
}
