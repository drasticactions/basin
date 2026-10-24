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
}
