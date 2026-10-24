namespace Basin.Scene;

/// <summary>
/// Supplies the color lookup table that converts a buffer node from its color description to the output's.
/// </summary>
public interface IColorLutTable
{
    /// <summary>
    /// Gets the lookup table to apply when the renderer draws a node.
    /// </summary>
    /// <param name="node">The buffer node to draw.</param>
    /// <returns>The table, or <see langword="null"/> when the node needs no conversion.</returns>
    IColorLut? LutFor(SceneBuffer node);

    /// <summary>
    /// Gets the table from <see cref="Capabilities.ImageDescription.SdrDefault"/> to the output, for the rects and meshes that the compositor draws.
    /// The default is <see langword="null"/>, which draws those colors as they are.
    /// </summary>
    IColorLut? SolidColorLut => null;
}
