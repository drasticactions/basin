namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects how a part picks one image from its numbered image files. The values are the uxtheme <c>IMAGESELECTTYPE</c> values.
/// </summary>
public enum ImageSelectType
{
    /// <summary>
    /// The part always uses its main image.
    /// </summary>
    None = 0,
    /// <summary>
    /// The part picks the largest image that fits the destination.
    /// </summary>
    Size = 1,
    /// <summary>
    /// The part picks the image whose minimum DPI matches the output DPI.
    /// </summary>
    Dpi = 2,
}
