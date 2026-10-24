using Basin.Capabilities;

namespace Basin.Frames.Metacity;

/// <summary>
/// The layout of one frame that <see cref="MetacityPainter.Layout(in MetacityFrameInput, int, int, MetacityFrameGeometry)"/> wrote last. All values are logical pixels on the frame surface.
/// </summary>
public sealed class MetacityFrameGeometry
{
    internal const int MaxMiddleBackgrounds = MetacityButtonLayout.MaxButtonsPerCorner - 2;

    internal readonly MetacityButtonSpace[] Buttons = new MetacityButtonSpace[(int)MetacityButtonType.Count];
    internal readonly Box[] LeftMiddleBackgrounds = new Box[MaxMiddleBackgrounds];
    internal readonly Box[] RightMiddleBackgrounds = new Box[MaxMiddleBackgrounds];
    internal readonly MetacityButtonFunction[] LeftFunctions = new MetacityButtonFunction[MetacityButtonLayout.MaxButtonsPerCorner];
    internal readonly MetacityButtonFunction[] RightFunctions = new MetacityButtonFunction[MetacityButtonLayout.MaxButtonsPerCorner];

    /// <summary>
    /// Gets the frame thickness around the client area.
    /// </summary>
    public FrameInsets Borders { get; internal set; }

    /// <summary>
    /// Gets the frame width, the client width plus the side borders.
    /// </summary>
    public int Width { get; internal set; }

    /// <summary>
    /// Gets the frame height. A shaded frame has only its borders.
    /// </summary>
    public int Height { get; internal set; }

    /// <summary>
    /// Gets the title area between the button groups. It is empty when the buttons leave no room.
    /// </summary>
    public Box TitleRect { get; internal set; }

    /// <summary>
    /// Gets the theme's <c>left_titlebar_edge</c>.
    /// </summary>
    public int LeftTitlebarEdge { get; internal set; }

    /// <summary>
    /// Gets the theme's <c>right_titlebar_edge</c>.
    /// </summary>
    public int RightTitlebarEdge { get; internal set; }

    /// <summary>
    /// Gets the top of the theme's <c>title_border</c>.
    /// </summary>
    public int TopTitlebarEdge { get; internal set; }

    /// <summary>
    /// Gets the bottom of the theme's <c>title_border</c>.
    /// </summary>
    public int BottomTitlebarEdge { get; internal set; }

    /// <summary>
    /// Gets the top-left corner radius. It is 0 when the two borders that meet there are too thin to round, as marco does.
    /// </summary>
    public int TopLeftRadius { get; internal set; }

    /// <summary>
    /// Gets the top-right corner radius, or 0 when the corner is not rounded.
    /// </summary>
    public int TopRightRadius { get; internal set; }

    /// <summary>
    /// Gets the bottom-left corner radius, or 0 when the corner is not rounded.
    /// </summary>
    public int BottomLeftRadius { get; internal set; }

    /// <summary>
    /// Gets the bottom-right corner radius, or 0 when the corner is not rounded.
    /// </summary>
    public int BottomRightRadius { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether any corner radius is above 0.
    /// </summary>
    public bool HasRoundedCorner => TopLeftRadius > 0 || TopRightRadius > 0 || BottomLeftRadius > 0 || BottomRightRadius > 0;

    internal int LeftCount { get; set; }

    internal int RightCount { get; set; }

    internal Box LeftLeftBackground { get; set; }

    internal Box LeftRightBackground { get; set; }

    internal Box LeftSingleBackground { get; set; }

    internal Box RightLeftBackground { get; set; }

    internal Box RightRightBackground { get; set; }

    internal Box RightSingleBackground { get; set; }

    /// <summary>
    /// Gets the drawn area of the button for a part.
    /// </summary>
    /// <param name="part">The button part.</param>
    /// <returns>The area, or an empty box when the layout shows no button for the part.</returns>
    public Box VisibleRect(FramePart part)
    {
        var box = default(Box);
        for (var type = MetacityButtonType.Close; type < MetacityButtonType.Count; type++)
        {
            if (PartOf(type) == part && !Buttons[(int)type].Visible.IsEmpty)
            {
                box = Buttons[(int)type].Visible;
            }
        }

        return box;
    }

    /// <summary>
    /// Gets the area where the button for a part takes input. For a maximized or tiled window, the button at the screen edge reaches the frame corner.
    /// </summary>
    /// <param name="part">The button part.</param>
    /// <returns>The area, or an empty box when the layout shows no button for the part.</returns>
    public Box ClickableRect(FramePart part)
    {
        var box = default(Box);
        for (var type = MetacityButtonType.Close; type < MetacityButtonType.Count; type++)
        {
            if (PartOf(type) == part && !Buttons[(int)type].Clickable.IsEmpty)
            {
                box = Buttons[(int)type].Clickable;
            }
        }

        return box;
    }

    /// <summary>
    /// Finds the button whose clickable area holds a point.
    /// </summary>
    /// <param name="x">The horizontal position in logical pixels.</param>
    /// <param name="y">The vertical position in logical pixels.</param>
    /// <returns>The button's part, or <see cref="FramePart.None"/> when no button is at the point.</returns>
    public FramePart ButtonAt(double x, double y)
    {
        for (var i = 0; i < LeftCount; i++)
        {
            var type = TypeOf(LeftFunctions[i]);
            if (Contains(Buttons[(int)type].Clickable, x, y))
            {
                return PartOf(type);
            }
        }

        for (var i = 0; i < RightCount; i++)
        {
            var type = TypeOf(RightFunctions[i]);
            if (Contains(Buttons[(int)type].Clickable, x, y))
            {
                return PartOf(type);
            }
        }

        return FramePart.None;
    }

    internal static bool Contains(in Box box, double x, double y) =>
        !box.IsEmpty && x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom;

    internal static FramePart PartOf(MetacityButtonType type) => type switch
    {
        MetacityButtonType.Close => FramePart.Close,
        MetacityButtonType.Maximize => FramePart.Maximize,
        MetacityButtonType.Minimize => FramePart.Minimize,
        MetacityButtonType.Menu => FramePart.Menu,
        MetacityButtonType.Shade or MetacityButtonType.Unshade => FramePart.Shade,
        MetacityButtonType.Above or MetacityButtonType.Unabove => FramePart.Above,
        MetacityButtonType.Stick or MetacityButtonType.Unstick => FramePart.Stick,
        _ => FramePart.None,
    };

    internal static MetacityButtonType TypeOf(MetacityButtonFunction function) => function switch
    {
        MetacityButtonFunction.Shade => MetacityButtonType.Shade,
        MetacityButtonFunction.Above => MetacityButtonType.Above,
        MetacityButtonFunction.Stick => MetacityButtonType.Stick,
        MetacityButtonFunction.Unshade => MetacityButtonType.Unshade,
        MetacityButtonFunction.Unabove => MetacityButtonType.Unabove,
        MetacityButtonFunction.Unstick => MetacityButtonType.Unstick,
        MetacityButtonFunction.Menu => MetacityButtonType.Menu,
        MetacityButtonFunction.AppMenu => MetacityButtonType.AppMenu,
        MetacityButtonFunction.Minimize => MetacityButtonType.Minimize,
        MetacityButtonFunction.Maximize => MetacityButtonType.Maximize,
        MetacityButtonFunction.Close => MetacityButtonType.Close,
        _ => MetacityButtonType.Count,
    };

    internal Box BackgroundRect(MetacityButtonType type, int middleOffset) => type switch
    {
        MetacityButtonType.LeftLeftBackground => LeftLeftBackground,
        MetacityButtonType.LeftMiddleBackground => LeftMiddleBackgrounds[middleOffset],
        MetacityButtonType.LeftRightBackground => LeftRightBackground,
        MetacityButtonType.LeftSingleBackground => LeftSingleBackground,
        MetacityButtonType.RightLeftBackground => RightLeftBackground,
        MetacityButtonType.RightMiddleBackground => RightMiddleBackgrounds[middleOffset],
        MetacityButtonType.RightRightBackground => RightRightBackground,
        MetacityButtonType.RightSingleBackground => RightSingleBackground,
        _ => Buttons[(int)type].Visible,
    };

    internal void Clear()
    {
        Array.Clear(Buttons);
        Array.Clear(LeftMiddleBackgrounds);
        Array.Clear(RightMiddleBackgrounds);
        LeftLeftBackground = default;
        LeftRightBackground = default;
        LeftSingleBackground = default;
        RightLeftBackground = default;
        RightRightBackground = default;
        RightSingleBackground = default;
        LeftCount = 0;
        RightCount = 0;
    }
}
