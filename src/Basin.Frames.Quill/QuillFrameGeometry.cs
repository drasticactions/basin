using Basin.Capabilities;
using Pixman;

namespace Basin.Frames.Quill;

/// <summary>
/// The layout of the last frame that a <see cref="QuillFrameRenderer"/> drew. All values are logical pixels on the frame surface.
/// </summary>
public sealed class QuillFrameGeometry
{
    private readonly Box[] _buttons = new Box[6];

    /// <summary>
    /// Gets the frame width: the client width and both side borders.
    /// </summary>
    public int Width { get; internal set; }

    /// <summary>
    /// Gets the frame height: the client height, the title bar and the borders. A shaded frame has no bottom border.
    /// </summary>
    public int Height { get; internal set; }

    /// <summary>
    /// Gets the thickness of the side, top and bottom borders.
    /// </summary>
    public int Border { get; internal set; }

    /// <summary>
    /// Gets the height of the title bar below the top border.
    /// </summary>
    public int TitleHeight { get; internal set; }

    /// <summary>
    /// Gets the title text box in the last layout, in frame surface coordinates. It is empty when the part is not shown or has no room.
    /// </summary>
    public Box Title { get; internal set; }

    /// <summary>
    /// Gets the window icon box in the last layout, in frame surface coordinates. It is empty when the window has a menu button in that place.
    /// </summary>
    public Box Icon { get; internal set; }

    /// <summary>
    /// Gets the window menu button box in the last layout, in frame surface coordinates. It is empty when the part is not shown or has no room.
    /// </summary>
    public Box Menu { get; internal set; }

    /// <summary>
    /// Gets the close button box in the last layout, in frame surface coordinates. It is empty when the part is not shown or has no room.
    /// </summary>
    public Box Close
    {
        get => _buttons[0];
        internal set => _buttons[0] = value;
    }

    /// <summary>
    /// Gets the maximize button box in the last layout, in frame surface coordinates. It is empty when the part is not shown or has no room.
    /// </summary>
    public Box Maximize
    {
        get => _buttons[1];
        internal set => _buttons[1] = value;
    }

    /// <summary>
    /// Gets the minimize button box in the last layout, in frame surface coordinates. It is empty when the part is not shown or has no room.
    /// </summary>
    public Box Minimize
    {
        get => _buttons[2];
        internal set => _buttons[2] = value;
    }

    /// <summary>
    /// Gets the shade toggle box in the last layout, in frame surface coordinates. It is empty when the part is not shown or has no room.
    /// </summary>
    public Box Shade
    {
        get => _buttons[3];
        internal set => _buttons[3] = value;
    }

    /// <summary>
    /// Gets the always-on-top toggle box in the last layout, in frame surface coordinates. It is empty when the part is not shown or has no room.
    /// </summary>
    public Box Above
    {
        get => _buttons[4];
        internal set => _buttons[4] = value;
    }

    /// <summary>
    /// Gets the all-workspaces toggle box in the last layout, in frame surface coordinates. It is empty when the part is not shown or has no room.
    /// </summary>
    public Box Stick
    {
        get => _buttons[5];
        internal set => _buttons[5] = value;
    }

    /// <summary>
    /// Gets a value indicating whether the theme gave the frame rounded corners in the last layout.
    /// </summary>
    public bool HasRoundedCorner { get; internal set; }

    /// <summary>
    /// Gives the box of a title-bar part in the last layout.
    /// </summary>
    /// <param name="part">The part to look up.</param>
    /// <returns>The box, or an empty box for a border part, <see cref="FramePart.None"/> or a part that is not shown.</returns>
    public Box BoundsOf(FramePart part) => part switch
    {
        FramePart.Title => Title,
        FramePart.Icon => Icon,
        FramePart.Menu => Menu,
        FramePart.Close => Close,
        FramePart.Maximize => Maximize,
        FramePart.Minimize => Minimize,
        FramePart.Shade => Shade,
        FramePart.Above => Above,
        FramePart.Stick => Stick,
        _ => default,
    };

    internal void Reset()
    {
        Title = default;
        Icon = default;
        Menu = default;
        for (var i = 0; i < _buttons.Length; i++)
        {
            _buttons[i] = default;
        }
    }
}
