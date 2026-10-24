using System.Drawing;
using Basin.Capabilities;

namespace Basin.Frames.MsStyles;

/// <summary>
/// One caption button of a laid-out frame.
/// </summary>
public struct MsStylesFrameButton
{
    /// <summary>
    /// The frame part that the button acts as.
    /// </summary>
    public FramePart Part;
    /// <summary>
    /// The theme part number that draws the button, in the Window class or, for the glass look, in the DWMWindow class.
    /// </summary>
    public int WindowPart;
    /// <summary>
    /// The button area in logical pixels on the frame surface.
    /// </summary>
    public RectangleF Bounds;
    /// <summary>
    /// A value indicating whether the button reacts to input. A disabled button is drawn in its disabled state.
    /// </summary>
    public bool Enabled;
}
