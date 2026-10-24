namespace Basin.Capabilities;

/// <summary>
/// Lists the named color primaries, numbered as in <c>wp_color_manager_v1.primaries</c>.
/// </summary>
public enum ColorPrimaries : uint
{
    /// <summary>
    /// The sRGB and BT.709 primaries.
    /// </summary>
    Srgb = 1,
    /// <summary>
    /// The PAL-M primaries (BT.470 System M).
    /// </summary>
    PalM = 2,
    /// <summary>
    /// The PAL primaries (BT.601 625-line).
    /// </summary>
    Pal = 3,
    /// <summary>
    /// The NTSC primaries (BT.601 525-line, SMPTE 170M).
    /// </summary>
    Ntsc = 4,
    /// <summary>
    /// The generic film primaries with illuminant C.
    /// </summary>
    GenericFilm = 5,
    /// <summary>
    /// The BT.2020 and BT.2100 primaries.
    /// </summary>
    Bt2020 = 6,
    /// <summary>
    /// The CIE 1931 XYZ space with an equal-energy white point.
    /// </summary>
    Cie1931Xyz = 7,
    /// <summary>
    /// The DCI-P3 primaries with the DCI white point.
    /// </summary>
    DciP3 = 8,
    /// <summary>
    /// The Display P3 primaries with the D65 white point.
    /// </summary>
    DisplayP3 = 9,
    /// <summary>
    /// The Adobe RGB (1998) primaries.
    /// </summary>
    AdobeRgb = 10,
}
