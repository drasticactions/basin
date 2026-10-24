namespace Basin.Capabilities;

/// <summary>
/// Maps a <see cref="CursorShape"/> to the cursor name that X cursor themes use.
/// </summary>
public static class CursorShapeNames
{
    /// <summary>
    /// Gets the X cursor theme name for a shape, for example <c>hand2</c> for <see cref="CursorShape.Pointer"/>.
    /// An unknown value and <see cref="CursorShape.Default"/> give <c>left_ptr</c>.
    /// </summary>
    /// <param name="shape">The shape to name.</param>
    /// <returns>The theme cursor name. It is never <see langword="null"/>.</returns>
    public static string NameOf(CursorShape shape) => shape switch
    {
        CursorShape.ContextMenu => "context-menu",
        CursorShape.Help => "question_arrow",
        CursorShape.Pointer => "hand2",
        CursorShape.Progress => "left_ptr_watch",
        CursorShape.Wait => "watch",
        CursorShape.Cell => "plus",
        CursorShape.Crosshair => "crosshair",
        CursorShape.Text => "xterm",
        CursorShape.VerticalText => "vertical-text",
        CursorShape.Alias => "dnd-link",
        CursorShape.Copy => "dnd-copy",
        CursorShape.Move => "fleur",
        CursorShape.NoDrop => "dnd-none",
        CursorShape.NotAllowed => "crossed_circle",
        CursorShape.Grab => "hand1",
        CursorShape.Grabbing => "grabbing",
        CursorShape.EResize => "right_side",
        CursorShape.NResize => "top_side",
        CursorShape.NeResize => "top_right_corner",
        CursorShape.NwResize => "top_left_corner",
        CursorShape.SResize => "bottom_side",
        CursorShape.SeResize => "bottom_right_corner",
        CursorShape.SwResize => "bottom_left_corner",
        CursorShape.WResize => "left_side",
        CursorShape.EwResize => "sb_h_double_arrow",
        CursorShape.NsResize => "sb_v_double_arrow",
        CursorShape.NeswResize => "fd_double_arrow",
        CursorShape.NwseResize => "bd_double_arrow",
        CursorShape.ColResize => "sb_h_double_arrow",
        CursorShape.RowResize => "sb_v_double_arrow",
        CursorShape.AllScroll => "fleur",
        CursorShape.ZoomIn => "zoom-in",
        CursorShape.ZoomOut => "zoom-out",
        CursorShape.DndAsk => "dnd-ask",
        CursorShape.AllResize => "all-resize",
        _ => "left_ptr",
    };
}
