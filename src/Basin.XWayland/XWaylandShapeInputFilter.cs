using Basin.Scene;

namespace Basin.XWayland;

public sealed class XWaylandShapeInputFilter(XWaylandWindow window) : ISceneInputFilter
{
    public bool AcceptsInputAt(SceneBuffer buffer, double x, double y)
    {
        var scale = window.Surface?.ServerScale ?? 1.0;
        var px = (int)Math.Floor(x * scale);
        var py = (int)Math.Floor(y * scale);
        return (window.InputShape is not { } input || input.Contains(px, py))
            && (window.BoundingShape is not { } bounding || bounding.Contains(px, py));
    }
}
