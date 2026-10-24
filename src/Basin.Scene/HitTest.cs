namespace Basin.Scene;

/// <content>
/// Hit testing on the scene graph.
/// </content>
public sealed partial class Scene
{
    /// <summary>
    /// Finds the topmost enabled node that accepts input at a scene point. Clips, transforms and input regions apply. A <see cref="SceneRect"/> is a hit anywhere in its size.
    /// </summary>
    /// <param name="x">The horizontal scene coordinate, in logical pixels.</param>
    /// <param name="y">The vertical scene coordinate, in logical pixels.</param>
    /// <returns>The node and the point in its local coordinates, or <see langword="null"/> when nothing is there.</returns>
    public SceneHit? NodeAt(double x, double y) => NodeAt(Root, x - Root.X, y - Root.Y);

    /// <summary>
    /// Finds the topmost surface buffer at a scene point. A <see cref="SceneRect"/> with <see cref="SceneRect.BlocksInput"/> set hides the surfaces below it.
    /// </summary>
    /// <param name="x">The horizontal scene coordinate, in logical pixels.</param>
    /// <param name="y">The vertical scene coordinate, in logical pixels.</param>
    /// <returns>The hit on a <see cref="SceneBuffer"/>, or <see langword="null"/> when no surface is there or a blocking rectangle is on top.</returns>
    public SceneHit? SurfaceAt(double x, double y) =>
        NodeAt(Root, x - Root.X, y - Root.Y, surfacesOnly: true) is { Node: SceneBuffer } hit ? hit : null;

    /// <summary>
    /// Maps a scene point into the local coordinates of a surface that the scene shows, through every transform above it.
    /// </summary>
    /// <param name="surface">The surface to map into.</param>
    /// <param name="x">The horizontal scene coordinate, in logical pixels.</param>
    /// <param name="y">The vertical scene coordinate, in logical pixels.</param>
    /// <param name="surfaceX">The horizontal surface-local coordinate, or 0 on failure.</param>
    /// <param name="surfaceY">The vertical surface-local coordinate, or 0 on failure.</param>
    /// <returns><see langword="false"/> when the scene has no live, enabled node for <paramref name="surface"/>, or a transform on the path cannot be inverted.</returns>
    public bool TryMapToSurface(Surface surface, double x, double y, out double surfaceX, out double surfaceY)
    {
        for (var i = 0; i < _surfaces.Count; i++)
        {
            var scene = _surfaces[i];
            if (ReferenceEquals(scene.Surface, surface) && !scene.IsDestroyed && scene.Tree.Enabled)
            {
                return scene.Content.TryMapSceneToLocal(x, y, out surfaceX, out surfaceY);
            }
        }

        surfaceX = 0;
        surfaceY = 0;
        return false;
    }

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

                case SceneRect rect when (!surfacesOnly || rect.BlocksInput) &&
                    localX >= 0 && localY >= 0 && localX < rect.Width && localY < rect.Height:
                    return new SceneHit(rect, localX, localY);
            }
        }

        return null;
    }
}
