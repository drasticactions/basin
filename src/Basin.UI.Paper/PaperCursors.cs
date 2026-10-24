using Prowl.PaperUI;

namespace Basin.UI.Paper;

/// <summary>
/// Maps Paper cursors to xcursor names.
/// </summary>
public static class PaperCursors
{
    /// <summary>
    /// Gets the xcursor name for a Paper cursor.
    /// </summary>
    /// <param name="cursor">The cursor that Paper asked for.</param>
    /// <returns>The xcursor name. A cursor with no match gives <c>default</c>.</returns>
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
