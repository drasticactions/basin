using Basin.Capabilities;

namespace Basin.Frames.Metacity;

public sealed class MetacityFrameGeometry
{
    internal const int MaxMiddleBackgrounds = MetacityButtonLayout.MaxButtonsPerCorner - 2;

    internal readonly MetacityButtonSpace[] Buttons = new MetacityButtonSpace[(int)MetacityButtonType.Count];
    internal readonly Box[] LeftMiddleBackgrounds = new Box[MaxMiddleBackgrounds];
    internal readonly Box[] RightMiddleBackgrounds = new Box[MaxMiddleBackgrounds];
    internal readonly MetacityButtonFunction[] LeftFunctions = new MetacityButtonFunction[MetacityButtonLayout.MaxButtonsPerCorner];
    internal readonly MetacityButtonFunction[] RightFunctions = new MetacityButtonFunction[MetacityButtonLayout.MaxButtonsPerCorner];

    public FrameInsets Borders { get; internal set; }

    public int Width { get; internal set; }

    public int Height { get; internal set; }

    public Box TitleRect { get; internal set; }

    public int LeftTitlebarEdge { get; internal set; }

    public int RightTitlebarEdge { get; internal set; }

    public int TopTitlebarEdge { get; internal set; }

    public int BottomTitlebarEdge { get; internal set; }

    public int TopLeftRadius { get; internal set; }

    public int TopRightRadius { get; internal set; }

    public int BottomLeftRadius { get; internal set; }

    public int BottomRightRadius { get; internal set; }

    public bool HasRoundedCorner => TopLeftRadius > 0 || TopRightRadius > 0 || BottomLeftRadius > 0 || BottomRightRadius > 0;

    internal int LeftCount { get; set; }

    internal int RightCount { get; set; }

    internal Box LeftLeftBackground { get; set; }

    internal Box LeftRightBackground { get; set; }

    internal Box LeftSingleBackground { get; set; }

    internal Box RightLeftBackground { get; set; }

    internal Box RightRightBackground { get; set; }

    internal Box RightSingleBackground { get; set; }

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
