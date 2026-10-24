namespace Basin.Scene;

/// <summary>
/// Narrows where a <see cref="SceneBuffer"/> accepts input, beyond its size and input region.
/// </summary>
public interface ISceneInputFilter
{
    /// <summary>
    /// Checks whether a buffer node takes input at a point.
    /// </summary>
    /// <param name="buffer">The node that hit testing reached.</param>
    /// <param name="x">The horizontal coordinate, local to <paramref name="buffer"/>.</param>
    /// <param name="y">The vertical coordinate, local to <paramref name="buffer"/>.</param>
    /// <returns><see langword="false"/> to let the hit fall through to the nodes below.</returns>
    bool AcceptsInputAt(SceneBuffer buffer, double x, double y);
}
