namespace Basin.Themes.MsStyles;

/// <summary>
/// Identifies the value type of a theme property. The values are the uxtheme <c>TMT_</c> primitive type IDs.
/// </summary>
public enum ThemePrimitive
{
    /// <summary>
    /// The property has no known type, or the lookup found no property.
    /// </summary>
    None = 0,
    /// <summary>
    /// The value is an enumeration value.
    /// </summary>
    Enum = 200,
    /// <summary>
    /// The value is text.
    /// </summary>
    String = 201,
    /// <summary>
    /// The value is an integer.
    /// </summary>
    Int = 202,
    /// <summary>
    /// The value is a Boolean, stored as an integer.
    /// </summary>
    Bool = 203,
    /// <summary>
    /// The value is an RGB color.
    /// </summary>
    Color = 204,
    /// <summary>
    /// The value is four margins: left, right, top and bottom.
    /// </summary>
    Margins = 205,
    /// <summary>
    /// The value is the path of an image in the theme.
    /// </summary>
    Filename = 206,
    /// <summary>
    /// The value is a size in pixels.
    /// </summary>
    Size = 207,
    /// <summary>
    /// The value is an X and Y position.
    /// </summary>
    Position = 208,
    /// <summary>
    /// The value is a rectangle: left, top, right and bottom.
    /// </summary>
    Rect = 209,
    /// <summary>
    /// The value is a font.
    /// </summary>
    Font = 210,
    /// <summary>
    /// The value is a list of integers.
    /// </summary>
    IntList = 211,
    /// <summary>
    /// The value is a bitmap handle in the Windows API.
    /// </summary>
    HBitmap = 212,
    /// <summary>
    /// The value is a stream in the file, for example the DWM image atlas.
    /// </summary>
    DiskStream = 213,
    /// <summary>
    /// The value is a stream of bytes.
    /// </summary>
    Stream = 214,
    /// <summary>
    /// The value is a reference to a bitmap.
    /// </summary>
    BitmapRef = 215,
}
