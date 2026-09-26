namespace Basin.Scene;

public sealed partial class Scene
{
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

    public bool IsCaptureExcludedAt(double x, double y) => NodeAt(x, y) is { } hit && IsCaptureExcluded(hit.Node);

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
