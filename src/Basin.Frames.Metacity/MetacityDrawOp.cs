namespace Basin.Frames.Metacity;

internal sealed class MetacityDrawOp(MetacityDrawOpKind kind)
{
    public MetacityDrawOpKind Kind { get; } = kind;

    public MetacityColorSpec? Color { get; init; }

    public bool Filled { get; init; }

    public MetacityExpression? X { get; init; }

    public MetacityExpression? Y { get; init; }

    public MetacityExpression? Width { get; init; }

    public MetacityExpression? Height { get; init; }

    public MetacityExpression? X2 { get; init; }

    public MetacityExpression? Y2 { get; init; }

    public int LineWidth { get; init; }

    public int DashOn { get; init; }

    public int DashOff { get; init; }

    public double StartAngle { get; init; }

    public double ExtentAngle { get; init; }

    public MetacityAlphaSpec? Alpha { get; init; }

    public MetacityGradientSpec? Gradient { get; init; }

    public MetacityImageInfo? Image { get; init; }

    public MetacityColorSpec? Colorize { get; init; }

    public MetacityFillType FillType { get; init; }

    public MetacityStateFlag State { get; init; }

    public MetacityShadow Shadow { get; init; }

    public MetacityArrow Arrow { get; init; }

    public MetacityExpression? EllipsizeWidth { get; init; }

    public MetacityDrawOpList? OpList { get; init; }

    public MetacityExpression? TileXOffset { get; init; }

    public MetacityExpression? TileYOffset { get; init; }

    public MetacityExpression? TileWidth { get; init; }

    public MetacityExpression? TileHeight { get; init; }
}
