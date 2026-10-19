namespace Basin.Effects;

public readonly record struct GlassOptions
{
    public GlassOptions()
    {
    }

    public int Strength { get; init; } = 8;

    public BlurKernel Kernel { get; init; } = BlurKernel.DualKawase;

    public GlassPass Pass { get; init; } = GlassPass.Composite;

    public uint Tint { get; init; } = 0x60202832;

    public uint InactiveTint { get; init; } = 0x40202832;

    public double Saturation { get; init; } = 1.4;

    public double Noise { get; init; } = 0.03;

    public double Lens { get; init; } = 0.6;

    public double Specular { get; init; } = 0.5;

    public double LightAngle { get; init; } = 315;

    public double Glint { get; init; } = 0.6;

    public double GlintRadius { get; init; } = 80;

    public uint ContentBorder { get; init; } = 0x38FFFFFF;

    public uint InactiveContentBorder { get; init; } = 0x20FFFFFF;

    public double ContentBorderWidth { get; init; } = 1;
}
