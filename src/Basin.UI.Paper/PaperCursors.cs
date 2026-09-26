using Prowl.PaperUI;

namespace Basin.UI.Paper;

public static class PaperCursors
{
    public static string NameOf(PaperCursor cursor) => cursor switch
    {
        PaperCursor.Inherit => "default",
        PaperCursor.Default => "default",
        PaperCursor.Pointer => "pointer",
        PaperCursor.Grab => "grab",
        PaperCursor.Grabbing => "grabbing",
        PaperCursor.Text => "text",
        PaperCursor.Crosshair => "crosshair",
        PaperCursor.ResizeHorizontal => "ew-resize",
        PaperCursor.ResizeVertical => "ns-resize",
        PaperCursor.ResizeNWSE => "nwse-resize",
        PaperCursor.ResizeNESW => "nesw-resize",
        PaperCursor.ResizeAll => "move",
        PaperCursor.NotAllowed => "not-allowed",
        PaperCursor.Wait => "wait",
        PaperCursor.Help => "help",
        _ => "default",
    };
}
