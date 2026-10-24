namespace Basin.Scene;

/// <summary>
/// The result of a scene hit test.
/// </summary>
/// <param name="Node">The node that was hit.</param>
/// <param name="X">The horizontal hit position, local to <paramref name="Node"/>.</param>
/// <param name="Y">The vertical hit position, local to <paramref name="Node"/>.</param>
public readonly record struct SceneHit(SceneNode Node, double X, double Y)
{
    /// <summary>
    /// Gets the input surface of the hit node, or <see langword="null"/> when the node is not a surface buffer.
    /// </summary>
    public Surface? Surface => (Node as SceneBuffer)?.InputSurface;
}
