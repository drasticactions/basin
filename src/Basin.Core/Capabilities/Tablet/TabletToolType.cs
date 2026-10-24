namespace Basin.Capabilities;

/// <summary>
/// Lists the physical tool types. The values are the Linux input event codes of the matching tool buttons.
/// </summary>
public enum TabletToolType : uint
{
    /// <summary>
    /// A pen (<c>BTN_TOOL_PEN</c>).
    /// </summary>
    Pen = 0x140,
    /// <summary>
    /// An eraser (<c>BTN_TOOL_RUBBER</c>).
    /// </summary>
    Eraser = 0x141,
    /// <summary>
    /// A brush (<c>BTN_TOOL_BRUSH</c>).
    /// </summary>
    Brush = 0x142,
    /// <summary>
    /// A pencil (<c>BTN_TOOL_PENCIL</c>).
    /// </summary>
    Pencil = 0x143,
    /// <summary>
    /// An airbrush (<c>BTN_TOOL_AIRBRUSH</c>).
    /// </summary>
    Airbrush = 0x144,
    /// <summary>
    /// A finger (<c>BTN_TOOL_FINGER</c>).
    /// </summary>
    Finger = 0x145,
    /// <summary>
    /// A tablet mouse (<c>BTN_TOOL_MOUSE</c>).
    /// </summary>
    Mouse = 0x146,
    /// <summary>
    /// A lens cursor (<c>BTN_TOOL_LENS</c>).
    /// </summary>
    Lens = 0x147,
}
