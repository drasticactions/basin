using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

/// <summary>
/// Applies a layer-shell layout to the layer surfaces of one output: it configures their sizes and moves their scene trees.
/// </summary>
public static class LayerArrangement
{
    /// <summary>
    /// Arranges the surfaces in an output box. Surfaces whose scene tree is disabled take no space. A surface gets a configure only when its size changes or it is not mapped.
    /// </summary>
    /// <param name="outputBox">The output box in layout coordinates.</param>
    /// <param name="surfaces">The surfaces with their scene surfaces, or <see langword="null"/> for a surface that is not mapped.</param>
    /// <returns>The usable area that the exclusive zones leave.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surfaces"/> is <see langword="null"/>.</exception>
    public static Box Arrange(Box outputBox, IReadOnlyList<(LayerSurface Layer, SceneSurface? Scene)> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        var visible = new List<(LayerSurface Layer, SceneSurface? Scene)>(surfaces.Count);
        foreach (var entry in surfaces)
        {
            if (entry.Scene is { IsDestroyed: false } hidden && !hidden.Tree.Enabled)
            {
                continue;
            }

            visible.Add(entry);
        }

        var bare = new LayerSurface[visible.Count];
        for (var i = 0; i < visible.Count; i++)
        {
            bare[i] = visible[i].Layer;
        }

        var (placements, usable) = LayerLayout.Arrange(outputBox, bare);
        foreach (var placement in placements)
        {
            var (layer, scene) = visible[placement.Index];
            var current = layer.Surface.Current;
            if (!layer.IsMapped ||
                current.Width != placement.Box.Width || current.Height != placement.Box.Height)
            {
                layer.Configure(placement.Box.Width, placement.Box.Height);
            }

            scene?.Tree.SetPosition(outputBox.X + placement.Box.X, outputBox.Y + placement.Box.Y);
        }

        return usable;
    }
}
