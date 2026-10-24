namespace Basin.Themes.MsStyles;

/// <summary>
/// Holds an opaque RGB color from a theme.
/// </summary>
/// <param name="R">The red component.</param>
/// <param name="G">The green component.</param>
/// <param name="B">The blue component.</param>
public readonly record struct MsStylesColor(byte R, byte G, byte B)
{
    /// <summary>
    /// Converts a Windows <c>COLORREF</c> to a color. The format is <c>0x00BBGGRR</c>, and the high byte is ignored.
    /// </summary>
    /// <param name="colorRef">The <c>COLORREF</c> value.</param>
    /// <returns>The color with the red, green and blue bytes of the value.</returns>
    public static MsStylesColor FromColorRef(int colorRef) =>
        new((byte)(colorRef & 0xff), (byte)((colorRef >> 8) & 0xff), (byte)((colorRef >> 16) & 0xff));

    /// <summary>
    /// Converts the color to a Windows <c>COLORREF</c> in the format <c>0x00BBGGRR</c>.
    /// </summary>
    /// <returns>The <c>COLORREF</c> value, with the high byte 0.</returns>
    public int ToColorRef() => R | (G << 8) | (B << 16);

    /// <summary>
    /// Converts the color to a packed value in the format <c>0xAARRGGBB</c>. The color components are not premultiplied.
    /// </summary>
    /// <param name="alpha">The alpha byte. The default is opaque.</param>
    /// <returns>The packed ARGB value.</returns>
    public uint ToArgb(byte alpha = 255) => ((uint)alpha << 24) | ((uint)R << 16) | ((uint)G << 8) | B;
}
