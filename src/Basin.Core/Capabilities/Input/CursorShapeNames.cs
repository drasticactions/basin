namespace Basin.Capabilities;

public static class CursorShapeNames
{
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
