using System.Drawing;
using Basin.Capabilities;

namespace Basin.Frames.MsStyles;

/// <summary>
/// The layout of one frame that <see cref="MsStylesFramePainter.Layout"/> wrote last. All rectangles are logical pixels on the frame surface, outsets included.
/// </summary>
public sealed class MsStylesFrameGeometry
{
    private readonly MsStylesFrameButton[] _buttons = new MsStylesFrameButton[3];

    /// <summary>
    /// Gets the width of the frame surface, which is the window plus the outsets.
    /// </summary>
    public int Width { get; internal set; }

    /// <summary>
    /// Gets the height of the frame surface, which is the window plus the outsets.
    /// </summary>
    public int Height { get; internal set; }

    /// <summary>
    /// Gets the frame thickness around the client area.
    /// </summary>
    public FrameInsets Insets { get; internal set; }

    /// <summary>
    /// Gets the shadow margin outside the window. It is zero for every look except glass.
    /// </summary>
    public FrameInsets Outsets { get; internal set; }

    /// <summary>
    /// Gets the window, which is the client area plus the insets.
    /// </summary>
    public RectangleF WindowRect { get; internal set; }

    /// <summary>
    /// Gets the client area.
    /// </summary>
    public RectangleF ClientRect { get; internal set; }

    /// <summary>
    /// Gets the frame border width, the theme's sizing border plus its padded border plus 3.
    /// </summary>
    public int Border { get; internal set; }

    /// <summary>
    /// Gets the height of the caption band from the window top. A frame without a caption gives the border width.
    /// </summary>
    public int CaptionHeight { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the layout is for a maximized window, which has no side or bottom borders.
    /// </summary>
    public bool Maximized { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the layout is for a utility window, which uses the small caption and parts.
    /// </summary>
    public bool Small { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the frame has a caption. A border-only frame has none.
    /// </summary>
    public bool HasCaption { get; internal set; }

    /// <summary>
    /// Gets the caption band. For a frame without a caption, it is the top border.
    /// </summary>
    public RectangleF CaptionRect { get; internal set; }

    /// <summary>
    /// Gets the left border below the caption. It is empty for a maximized window.
    /// </summary>
    public RectangleF LeftRect { get; internal set; }

    /// <summary>
    /// Gets the right border below the caption. It is empty for a maximized window.
    /// </summary>
    public RectangleF RightRect { get; internal set; }

    /// <summary>
    /// Gets the bottom border. It is empty for a maximized window.
    /// </summary>
    public RectangleF BottomRect { get; internal set; }

    /// <summary>
    /// Gets the 16 by 16 window icon area. It is empty unless a normal window has an icon, an icon name or an application ID.
    /// </summary>
    public RectangleF IconRect { get; internal set; }

    /// <summary>
    /// Gets the area for the title text, between the icon and the first button. It is empty for a frame without a caption.
    /// </summary>
    public RectangleF TitleRect { get; internal set; }

    /// <summary>
    /// Gets the number of caption buttons, from 0 to 3.
    /// </summary>
    public int ButtonCount { get; internal set; }

    /// <summary>
    /// Gets the caption buttons from right to left, close first. The span is valid until the next layout.
    /// </summary>
    public ReadOnlySpan<MsStylesFrameButton> Buttons => _buttons.AsSpan(0, ButtonCount);

    internal ref MsStylesFrameButton Button(int index) => ref _buttons[index];

    /// <summary>
    /// Finds the enabled button at a point.
    /// </summary>
    /// <param name="x">The horizontal position in logical pixels on the frame surface.</param>
    /// <param name="y">The vertical position in logical pixels on the frame surface.</param>
    /// <returns>The button's part, or <see cref="FramePart.None"/> when no enabled button is at the point.</returns>
    public FramePart ButtonAt(double x, double y)
    {
        foreach (ref readonly var button in Buttons)
        {
            if (button.Enabled && x >= button.Bounds.Left && x < button.Bounds.Right && y >= button.Bounds.Top && y < button.Bounds.Bottom)
            {
                return button.Part;
            }
        }

        return FramePart.None;
    }

    /// <summary>
    /// Gets the area of a button, the title or the icon.
    /// </summary>
    /// <param name="part">The part to look up. <see cref="FramePart.Menu"/> and <see cref="FramePart.Icon"/> both give the icon area.</param>
    /// <returns>The area in logical pixels, or an empty rectangle for a part that the layout does not have.</returns>
    public RectangleF BoundsOf(FramePart part)
    {
        foreach (ref readonly var button in Buttons)
        {
            if (button.Part == part)
            {
                return button.Bounds;
            }
        }

        return part switch
        {
            FramePart.Title => TitleRect,
            FramePart.Menu or FramePart.Icon => IconRect,
            _ => RectangleF.Empty,
        };
    }
}
