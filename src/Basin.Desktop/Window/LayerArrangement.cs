using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

public static class LayerArrangement
{
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
