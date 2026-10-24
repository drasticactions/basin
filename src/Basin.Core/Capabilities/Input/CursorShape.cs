namespace Basin.Capabilities;

/// <summary>
/// A named cursor shape. The values are the wire values of the <c>wp_cursor_shape_device_v1</c> shape enum.
/// </summary>
public enum CursorShape : uint
{
    /// <summary>The default arrow pointer.</summary>
    Default = 1,
    /// <summary>A context menu is available for the object under the cursor.</summary>
    ContextMenu = 2,
    /// <summary>Help is available for the object under the cursor.</summary>
    Help = 3,
    /// <summary>A hand that shows a link or a clickable object.</summary>
    Pointer = 4,
    /// <summary>The program is busy, but the user can still interact with it.</summary>
    Progress = 5,
    /// <summary>The program is busy, and the user cannot interact with it.</summary>
    Wait = 6,
    /// <summary>A cell or a set of cells can be selected.</summary>
    Cell = 7,
    /// <summary>A simple crosshair.</summary>
    Crosshair = 8,
    /// <summary>Horizontal text can be selected.</summary>
    Text = 9,
    /// <summary>Vertical text can be selected.</summary>
    VerticalText = 10,
    /// <summary>A drag-and-drop operation will create an alias or a shortcut.</summary>
    Alias = 11,
    /// <summary>A drag-and-drop operation will copy the object.</summary>
    Copy = 12,
    /// <summary>A drag-and-drop operation will move the object.</summary>
    Move = 13,
    /// <summary>The current drop location does not accept the dragged object.</summary>
    NoDrop = 14,
    /// <summary>The requested action is not permitted.</summary>
    NotAllowed = 15,
    /// <summary>An open hand that shows that the object can be grabbed.</summary>
    Grab = 16,
    /// <summary>A closed hand that shows that the object is grabbed.</summary>
    Grabbing = 17,
    /// <summary>The east edge will move.</summary>
    EResize = 18,
    /// <summary>The north edge will move.</summary>
    NResize = 19,
    /// <summary>The north-east corner will move.</summary>
    NeResize = 20,
    /// <summary>The north-west corner will move.</summary>
    NwResize = 21,
    /// <summary>The south edge will move.</summary>
    SResize = 22,
    /// <summary>The south-east corner will move.</summary>
    SeResize = 23,
    /// <summary>The south-west corner will move.</summary>
    SwResize = 24,
    /// <summary>The west edge will move.</summary>
    WResize = 25,
    /// <summary>A resize in the east-west direction.</summary>
    EwResize = 26,
    /// <summary>A resize in the north-south direction.</summary>
    NsResize = 27,
    /// <summary>A resize in the north-east to south-west direction.</summary>
    NeswResize = 28,
    /// <summary>A resize in the north-west to south-east direction.</summary>
    NwseResize = 29,
    /// <summary>A column can be resized horizontally.</summary>
    ColResize = 30,
    /// <summary>A row can be resized vertically.</summary>
    RowResize = 31,
    /// <summary>The content can scroll in all directions.</summary>
    AllScroll = 32,
    /// <summary>The content can be zoomed in.</summary>
    ZoomIn = 33,
    /// <summary>The content can be zoomed out.</summary>
    ZoomOut = 34,
    /// <summary>A drag-and-drop operation will ask the user which action to take.</summary>
    DndAsk = 35,
    /// <summary>The object can be resized in all directions.</summary>
    AllResize = 36,
}
