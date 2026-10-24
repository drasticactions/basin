namespace Basin.Scene;

/// <content>
/// The capture-exclusion queries of the scene.
/// </content>
public sealed partial class Scene
{
    /// <summary>
    /// Checks whether a node or any of its ancestors sets <see cref="SceneNode.ExcludedFromCapture"/>.
    /// </summary>
    /// <param name="node">The node to check.</param>
    /// <returns><see langword="true"/> when screen capture must leave out the node.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public static bool IsCaptureExcluded(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        for (SceneNode? candidate = node; candidate is not null; candidate = candidate.Parent)
        {
            if (candidate.ExcludedFromCapture)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether the topmost input-accepting node at a scene point is excluded from capture.
    /// </summary>
    /// <param name="x">The horizontal scene coordinate, in logical pixels.</param>
    /// <param name="y">The vertical scene coordinate, in logical pixels.</param>
    /// <returns><see langword="true"/> when <see cref="NodeAt(double, double)"/> finds a node and that node is excluded. <see langword="false"/> when no node is there.</returns>
    public bool IsCaptureExcludedAt(double x, double y) => NodeAt(x, y) is { } hit && IsCaptureExcluded(hit.Node);

    /// <summary>
    /// Checks whether any enabled node in the scene sets <see cref="SceneNode.ExcludedFromCapture"/>. Disabled subtrees are skipped.
    /// </summary>
    /// <returns><see langword="true"/> when at least one enabled node is excluded from capture.</returns>
    public bool HasCaptureExcluded() => HasCaptureExcluded(Root);

    private static bool HasCaptureExcluded(SceneNode node)
    {
        if (!node.Enabled)
        {
            return false;
        }

        if (node.ExcludedFromCapture)
        {
            return true;
        }

        if (node is SceneTree tree)
        {
            var children = tree.Children;
            for (var i = 0; i < children.Count; i++)
            {
                if (HasCaptureExcluded(children[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
