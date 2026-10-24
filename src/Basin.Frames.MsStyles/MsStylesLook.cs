namespace Basin.Frames.MsStyles;

/// <summary>
/// The frame looks that an <see cref="MsStylesFrameStyle"/> can draw.
/// </summary>
public enum MsStylesLook
{
    /// <summary>
    /// Picks <see cref="ClassicXp"/> for an XP theme, and <see cref="Glass"/> for a later theme. A later theme without the DWMWindow class gets <see cref="Basic"/>.
    /// </summary>
    Auto,
    /// <summary>
    /// The XP frame from the Window class.
    /// </summary>
    ClassicXp,
    /// <summary>
    /// The Vista Basic frame from the Window class, with no composition.
    /// </summary>
    Basic,
    /// <summary>
    /// The composited Vista frame from the DWMWindow class, with a shadow and a translucent ring. It needs a theme in format 4.
    /// </summary>
    Glass,
}
