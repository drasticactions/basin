namespace Basin.Frames.Metacity;

internal sealed class MetacityGradientSpec(MetacityGradientType type)
{
    public MetacityGradientType Type { get; } = type;

    public List<MetacityColorSpec> Colors { get; } = [];
}
