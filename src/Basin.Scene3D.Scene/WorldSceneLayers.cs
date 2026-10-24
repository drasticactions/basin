using Basin.Scene;

namespace Basin.Scene3D.Scene;

/// <summary>
/// Flattens a window's scene tree into the <see cref="WorldSurfaceLayer"/> values that a pass draws on its canvas.
/// </summary>
public static class WorldSceneLayers
{
    /// <summary>
    /// Appends a layer to <paramref name="window"/> for each enabled buffer node with a buffer under <paramref name="tree"/>, in scene order. It does not clear the window's layers first.
    /// </summary>
    /// <param name="window">The window that receives the layers.</param>
    /// <param name="tree">The tree to flatten. Disabled nodes and their subtrees are skipped.</param>
    /// <param name="renderer">The 2D renderer whose textures the layers sample.</param>
    /// <param name="originX">The scene x coordinate of the main surface's origin. The layers are relative to it.</param>
    /// <param name="originY">The scene y coordinate of the main surface's origin.</param>
    /// <param name="level">The level of the next layer. Each layer takes one level and increments it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/>, <paramref name="tree"/> or <paramref name="renderer"/> is <see langword="null"/>.</exception>
    public static void Collect(WorldWindow window, SceneTree tree, IRenderer renderer, int originX, int originY, ref int level)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(renderer);
        var children = tree.Children;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (!child.Enabled)
            {
                continue;
            }

            if (child is SceneTree subtree)
            {
                Collect(window, subtree, renderer, originX, originY, ref level);
                continue;
            }

            if (child is not SceneBuffer buffer || buffer.Buffer is null)
            {
                continue;
            }

            var (x, y) = buffer.ScenePosition;
            var (width, height) = buffer.DisplaySize;
            window.AddLayer(new WorldSurfaceLayer
            {
                Surface = buffer.InputSurface,
                Texture = buffer.TextureFor(renderer),
                X = x - originX,
                Y = y - originY,
                Width = width,
                Height = height,
                Source = buffer.SourceBox,
                Level = level++,
                ContentVersion = buffer.ContentVersion,
                Input = buffer.InputEnabled && buffer.InputSurface is not null,
            });
        }
    }
}
