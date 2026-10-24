namespace Basin.Effects;

/// <summary>
/// The settings of a <see cref="DropShadowTexture"/>. Distances are in logical pixels. The defaults are the Breeze shadow.
/// </summary>
public readonly record struct DropShadowOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DropShadowOptions"/> struct with the Breeze defaults.
    /// </summary>
    public DropShadowOptions()
    {
    }

    /// <summary>
    /// Gets the wide ambient layer.
    /// </summary>
    public DropShadowLayer Primary { get; init; } = new(0, 0, 48, 0.8);

    /// <summary>
    /// Gets the narrow contact layer that composites over the primary layer.
    /// </summary>
    public DropShadowLayer Secondary { get; init; } = new(0, -6, 24, 0.2);

    /// <summary>
    /// Gets the horizontal offset of the composite shadow from the window.
    /// </summary>
    public double OffsetX { get; init; }

    /// <summary>
    /// Gets the vertical offset of the composite shadow from the window. A positive value moves it down.
    /// </summary>
    public double OffsetY { get; init; } = 12;

    /// <summary>
    /// Gets the corner radius of the window box that the texture punches out.
    /// </summary>
    public double CornerRadius { get; init; } = 5;

    /// <summary>
    /// Gets how far the shadow tucks under the window edge on every side, so that no seam shows at a fractional scale.
    /// </summary>
    public double Overlap { get; init; } = 3;

    /// <summary>
    /// Gets the opacity multiplier for both layers, from 0 to 1.
    /// </summary>
    public double Strength { get; init; } = 1;

    /// <summary>
    /// Gets the shadow color. Its alpha also multiplies the opacity.
    /// </summary>
    public RenderColor Color { get; init; } = RenderColor.Black;
}
