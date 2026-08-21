namespace Basin.Scene;

public sealed partial class Scene
{
    public SceneHit? NodeAt(double x, double y) => NodeAt(Root, x - Root.X, y - Root.Y);

    public SceneHit? SurfaceAt(double x, double y) =>
        NodeAt(Root, x - Root.X, y - Root.Y, surfacesOnly: true);

    private static SceneHit? NodeAt(SceneTree tree, double x, double y, bool surfacesOnly = false)
    {
        for (var i = tree.Children.Count - 1; i >= 0; i--)
        {
            var child = tree.Children[i];
            if (!child.Enabled)
            {
                continue;
            }

            var localX = x - child.X;
            var localY = y - child.Y;

            if (child.IsClipped && child is not SceneTransform)
            {
                var clip = child.ClipBox;
                if (localX < clip.X || localY < clip.Y || localX >= clip.Right || localY >= clip.Bottom)
                {
                    continue;
                }
            }

            switch (child)
            {
                case SceneTransform transform:
                    if (!transform.TryMapToLocal(localX, localY, out var mappedX, out var mappedY))
                    {
                        break;
                    }

                    if (transform.IsClipped)
                    {
                        var clip = transform.ClipBox;
                        if (mappedX < clip.X || mappedY < clip.Y || mappedX >= clip.Right || mappedY >= clip.Bottom)
                        {
                            break;
                        }
                    }

                    if (NodeAt(transform, mappedX, mappedY, surfacesOnly) is { } transformedHit)
                    {
                        return transformedHit;
                    }

                    break;

                case SceneTree subtree:
                    if (NodeAt(subtree, localX, localY, surfacesOnly) is { } hit)
                    {
                        return hit;
                    }

                    break;

                case SceneBuffer buffer when buffer.AcceptsInputAt(localX, localY):
                    if (!surfacesOnly || buffer.InputSurface is not null)
                    {
                        return new SceneHit(buffer, localX, localY);
                    }

                    break;

                case SceneRect rect when !surfacesOnly &&
                    localX >= 0 && localY >= 0 && localX < rect.Width && localY < rect.Height:
                    return new SceneHit(rect, localX, localY);
            }
        }

        return null;
    }
}
