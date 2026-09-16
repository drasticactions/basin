namespace Basin.Frames.Metacity;

internal sealed class MetacityFrameLayout
{
    public MetacityFrameLayout()
    {
    }

    public MetacityFrameLayout(MetacityFrameLayout parent)
    {
        LeftWidth = parent.LeftWidth;
        RightWidth = parent.RightWidth;
        BottomHeight = parent.BottomHeight;
        TitleBorder = parent.TitleBorder;
        TitleVerticalPad = parent.TitleVerticalPad;
        RightTitlebarEdge = parent.RightTitlebarEdge;
        LeftTitlebarEdge = parent.LeftTitlebarEdge;
        ButtonSizing = parent.ButtonSizing;
        ButtonAspect = parent.ButtonAspect;
        ButtonWidth = parent.ButtonWidth;
        ButtonHeight = parent.ButtonHeight;
        ButtonBorder = parent.ButtonBorder;
        TitleScale = parent.TitleScale;
        HasTitle = parent.HasTitle;
        HideButtons = parent.HideButtons;
        TopLeftRadius = parent.TopLeftRadius;
        TopRightRadius = parent.TopRightRadius;
        BottomLeftRadius = parent.BottomLeftRadius;
        BottomRightRadius = parent.BottomRightRadius;
    }

    public int LeftWidth { get; set; } = -1;

    public int RightWidth { get; set; } = -1;

    public int BottomHeight { get; set; } = -1;

    public MetacityBorder TitleBorder { get; set; } = MetacityBorder.Unset;

    public int TitleVerticalPad { get; set; } = -1;

    public int RightTitlebarEdge { get; set; } = -1;

    public int LeftTitlebarEdge { get; set; } = -1;

    public MetacityButtonSizing ButtonSizing { get; set; } = MetacityButtonSizing.Unset;

    public double ButtonAspect { get; set; } = 1.0;

    public int ButtonWidth { get; set; } = -1;

    public int ButtonHeight { get; set; } = -1;

    public MetacityBorder ButtonBorder { get; set; } = MetacityBorder.Unset;

    public double TitleScale { get; set; } = 1.0;

    public bool HasTitle { get; set; } = true;

    public bool HideButtons { get; set; }

    public int TopLeftRadius { get; set; }

    public int TopRightRadius { get; set; }

    public int BottomLeftRadius { get; set; }

    public int BottomRightRadius { get; set; }

    public string? Validate()
    {
        if (LeftWidth < 0)
        {
            return Missing("left_width");
        }

        if (RightWidth < 0)
        {
            return Missing("right_width");
        }

        if (BottomHeight < 0)
        {
            return Missing("bottom_height");
        }

        if (TitleBorder.UnsetSide is { } titleSide)
        {
            return $"frame geometry does not specify dimension \"{titleSide}\" for border \"title_border\"";
        }

        if (TitleVerticalPad < 0)
        {
            return Missing("title_vertical_pad");
        }

        if (RightTitlebarEdge < 0)
        {
            return Missing("right_titlebar_edge");
        }

        if (LeftTitlebarEdge < 0)
        {
            return Missing("left_titlebar_edge");
        }

        switch (ButtonSizing)
        {
            case MetacityButtonSizing.Aspect:
                if (ButtonAspect < 0.1 || ButtonAspect > 15.0)
                {
                    return $"Button aspect ratio {ButtonAspect} is not reasonable";
                }

                break;
            case MetacityButtonSizing.Fixed:
                if (ButtonWidth < 0)
                {
                    return Missing("button_width");
                }

                if (ButtonHeight < 0)
                {
                    return Missing("button_height");
                }

                break;
            default:
                return "Frame geometry does not specify size of buttons";
        }

        if (ButtonBorder.UnsetSide is { } buttonSide)
        {
            return $"frame geometry does not specify dimension \"{buttonSide}\" for border \"button_border\"";
        }

        return null;
    }

    private static string Missing(string name) => $"frame geometry does not specify \"{name}\" dimension";
}
