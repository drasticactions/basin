namespace Basin.Themes.MsStyles;

/// <summary>
/// Identifies the encoding of the bytes in an <see cref="MsStylesImage"/>.
/// </summary>
public enum MsStylesImageFormat
{
    /// <summary>
    /// The bytes are neither a PNG nor a bitmap that the reader recognizes.
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// The bytes are a Windows bitmap. They always start with a <c>BM</c> file header.
    /// </summary>
    Dib = 1,
    /// <summary>
    /// The bytes are a PNG image.
    /// </summary>
    Png = 2,
}
