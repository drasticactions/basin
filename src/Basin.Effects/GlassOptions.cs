namespace Basin.Effects;

/// <summary>
/// The settings of a glass backdrop effect. Colors are ARGB with alpha in the high byte.
/// </summary>
public readonly record struct GlassOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlassOptions"/> struct with the default glass.
    /// </summary>
    public GlassOptions()
    {
    }

    /// <summary>
    /// Gets the blur strength step under the glass.
    /// </summary>
    public int Strength { get; init; } = 8;

    /// <summary>
    /// Gets the blur kernel that the composite pass uses.
    /// </summary>
    public BlurKernel Kernel { get; init; } = BlurKernel.DualKawase;

    /// <summary>
    /// Gets the pass shape.
    /// </summary>
    public GlassPass Pass { get; init; } = GlassPass.Composite;

    /// <summary>
    /// Gets the tint at full emphasis. The tint alpha is the mix toward the tint color.
    /// </summary>
    public uint Tint { get; init; } = 0x60202832;

    /// <summary>
    /// Gets the tint at zero emphasis.
    /// </summary>
    public uint InactiveTint { get; init; } = 0x40202832;

    /// <summary>
    /// Gets the saturation factor applied to the blur before the tint.
    /// </summary>
    public double Saturation { get; init; } = 1.4;

    /// <summary>
    /// Gets the strength of the grain. Zero turns the grain off.
    /// </summary>
    public double Noise { get; init; } = 0.03;

    /// <summary>
    /// Gets how far the bevel pulls samples inward, as a fraction of the bevel width.
    /// </summary>
    public double Lens { get; init; } = 0.6;

    /// <summary>
    /// Gets the strength of the sheen and the lit rim at full emphasis. Zero emphasis halves it.
    /// </summary>
    public double Specular { get; init; } = 0.5;

    /// <summary>
    /// Gets the light direction in degrees in screen coordinates. 0 is to the right and 90 is down.
    /// </summary>
    public double LightAngle { get; init; } = 315;

    /// <summary>
    /// Gets the strength of the highlight spot at the highlight point of the shape.
    /// </summary>
    public double Glint { get; init; } = 0.6;

    /// <summary>
    /// Gets the radius of the highlight spot in logical pixels.
    /// </summary>
    public double GlintRadius { get; init; } = 80;

    /// <summary>
    /// Gets the color of the line around the client hole at full emphasis.
    /// </summary>
    public uint ContentBorder { get; init; } = 0x38FFFFFF;

    /// <summary>
    /// Gets the color of the line around the client hole at zero emphasis.
    /// </summary>
    public uint InactiveContentBorder { get; init; } = 0x20FFFFFF;

    /// <summary>
    /// Gets the width of the line around the client hole in logical pixels. Zero draws no line.
    /// </summary>
    public double ContentBorderWidth { get; init; } = 1;
}
