using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// Maps the edges of an interactive resize to a cursor shape.
/// </summary>
public static class EdgeCursors
{
    /// <summary>
    /// Gets the resize cursor for a set of edges. A corner gives a diagonal cursor and two opposite edges give a two-way cursor.
    /// </summary>
    /// <param name="edges">The edges that the resize moves.</param>
    /// <returns>The matching resize shape, or <see cref="WpCursorShapeDeviceV1.Shape.Default"/> for <see cref="Edges.None"/> and other combinations.</returns>
    public static WpCursorShapeDeviceV1.Shape ShapeFor(Edges edges) => edges switch
    {
        Edges.Top | Edges.Left => WpCursorShapeDeviceV1.Shape.NwResize,
        Edges.Top | Edges.Right => WpCursorShapeDeviceV1.Shape.NeResize,
        Edges.Bottom | Edges.Left => WpCursorShapeDeviceV1.Shape.SwResize,
        Edges.Bottom | Edges.Right => WpCursorShapeDeviceV1.Shape.SeResize,
        Edges.Left => WpCursorShapeDeviceV1.Shape.WResize,
        Edges.Right => WpCursorShapeDeviceV1.Shape.EResize,
        Edges.Top => WpCursorShapeDeviceV1.Shape.NResize,
        Edges.Bottom => WpCursorShapeDeviceV1.Shape.SResize,
        Edges.Left | Edges.Right => WpCursorShapeDeviceV1.Shape.EwResize,
        Edges.Top | Edges.Bottom => WpCursorShapeDeviceV1.Shape.NsResize,
        _ => WpCursorShapeDeviceV1.Shape.Default,
    };
}
