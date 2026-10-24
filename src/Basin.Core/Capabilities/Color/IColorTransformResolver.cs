namespace Basin.Capabilities;

/// <summary>
/// Supplies the conversion table from a source description to an output description. The implementation holds the renderer and caches the tables.
/// </summary>
public interface IColorTransformResolver
{
    /// <summary>
    /// Gets the conversion capability of the renderer behind the resolver. With <see cref="ColorTransformCapability.None"/>, the resolver cannot convert at all.
    /// </summary>
    ColorTransformCapability Capability { get; }

    /// <summary>
    /// Gets the table that converts content from one description into another.
    /// </summary>
    /// <param name="source">The description of the content.</param>
    /// <param name="output">The description of the space that the content composites into.</param>
    /// <returns>A table that the resolver owns, or <see langword="null"/> when no conversion is necessary or possible.</returns>
    IColorLut? Resolve(ImageDescription source, ImageDescription output);
}
