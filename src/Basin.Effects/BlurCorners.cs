namespace Basin.Effects;

public readonly record struct BlurCorners(
    double TopLeft,
    double TopRight,
    double BottomLeft,
    double BottomRight)
{
    public BlurCorners(double radius)
        : this(radius, radius, radius, radius)
    {
    }

    public bool IsSquare => TopLeft <= 0 && TopRight <= 0 && BottomLeft <= 0 && BottomRight <= 0;
}
