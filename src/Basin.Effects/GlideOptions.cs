namespace Basin.Effects;

public readonly record struct GlideOptions
{
    public GlideOptions()
    {
    }

    public double BaseMillis { get; init; } = 160;

    public FrustumEdge InEdge { get; init; } = FrustumEdge.Top;

    public double InAngle { get; init; } = 3.0;

    public double InDistance { get; init; } = 30.0;

    public double InOpacity { get; init; } = 0.4;

    public FrustumEdge OutEdge { get; init; } = FrustumEdge.Bottom;

    public double OutAngle { get; init; } = 3.0;

    public double OutDistance { get; init; } = 30.0;

    public double OutOpacity { get; init; } = 0.0;
}
