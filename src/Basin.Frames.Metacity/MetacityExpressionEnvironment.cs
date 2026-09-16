namespace Basin.Frames.Metacity;

internal struct MetacityExpressionEnvironment
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public int ObjectWidth;
    public int ObjectHeight;
    public int LeftWidth;
    public int RightWidth;
    public int TopHeight;
    public int BottomHeight;
    public int TitleWidth;
    public int TitleHeight;
    public int FrameXCenter;
    public int FrameYCenter;
    public int MiniIconWidth;
    public int MiniIconHeight;
    public int IconWidth;
    public int IconHeight;

    public readonly int Get(MetacityVariable variable) => variable switch
    {
        MetacityVariable.Width => Width,
        MetacityVariable.Height => Height,
        MetacityVariable.ObjectWidth => ObjectWidth,
        MetacityVariable.ObjectHeight => ObjectHeight,
        MetacityVariable.LeftWidth => LeftWidth,
        MetacityVariable.RightWidth => RightWidth,
        MetacityVariable.TopHeight => TopHeight,
        MetacityVariable.BottomHeight => BottomHeight,
        MetacityVariable.MiniIconWidth => MiniIconWidth,
        MetacityVariable.MiniIconHeight => MiniIconHeight,
        MetacityVariable.IconWidth => IconWidth,
        MetacityVariable.IconHeight => IconHeight,
        MetacityVariable.TitleWidth => TitleWidth,
        MetacityVariable.TitleHeight => TitleHeight,
        MetacityVariable.FrameXCenter => FrameXCenter,
        MetacityVariable.FrameYCenter => FrameYCenter,
        _ => 0,
    };
}
