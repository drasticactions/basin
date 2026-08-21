using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

public static class LayerArrangement
{
    public static Box Arrange(Box outputBox, IReadOnlyList<(LayerSurface Layer, SceneSurface? Scene)> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        var bare = new LayerSurface[surfaces.Count];
        for (var i = 0; i < surfaces.Count; i++)
        {
            bare[i] = surfaces[i].Layer;
        }

        var (placements, usable) = LayerLayout.Arrange(outputBox, bare);
        foreach (var placement in placements)
        {
            var (layer, scene) = surfaces[placement.Index];
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
