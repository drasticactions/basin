using Basin.Scene;

namespace Basin.XWayland;

/// <summary>
/// Lets a scene buffer take input only inside the X Shape input and bounding regions of an X11 window.
/// </summary>
/// <param name="window">The window whose shapes the filter reads on each hit test.</param>
public sealed class XWaylandShapeInputFilter(XWaylandWindow window) : ISceneInputFilter
{
    /// <inheritdoc/>
    public bool AcceptsInputAt(SceneBuffer buffer, double x, double y)
    {
        var scale = window.Surface?.ServerScale ?? 1.0;
        var px = (int)Math.Floor(x * scale);
        var py = (int)Math.Floor(y * scale);
        return (window.InputShape is not { } input || input.Contains(px, py))
            && (window.BoundingShape is not { } bounding || bounding.Contains(px, py));
    }
}
