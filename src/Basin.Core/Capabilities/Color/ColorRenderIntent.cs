namespace Basin.Capabilities;

/// <summary>
/// Lists the render intents for a color conversion, numbered as in <c>wp_color_manager_v1.render_intent</c>.
/// </summary>
public enum ColorRenderIntent : uint
{
    /// <summary>
    /// Compresses the whole source gamut into the destination gamut.
    /// </summary>
    Perceptual = 0,
    /// <summary>
    /// Keeps in-gamut colors and clips the others, with white point adaptation.
    /// </summary>
    Relative = 1,
    /// <summary>
    /// Keeps saturation at the cost of hue and lightness accuracy.
    /// </summary>
    Saturation = 2,
    /// <summary>
    /// Keeps in-gamut colors and clips the others, without white point adaptation.
    /// </summary>
    Absolute = 3,
    /// <summary>
    /// Relative colorimetric with black point compensation.
    /// </summary>
    RelativeBpc = 4,

    /// <summary>
    /// Absolute colorimetric without chromatic adaptation of the white point.
    /// </summary>
    AbsoluteNoAdaptation = 5,
}
