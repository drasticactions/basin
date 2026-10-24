namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects the kind of shadow under theme text. The values are the uxtheme <c>TEXTSHADOWTYPE</c> values.
/// </summary>
public enum TextShadowType
{
    /// <summary>
    /// The text has no shadow.
    /// </summary>
    None = 0,
    /// <summary>
    /// The text has one offset shadow.
    /// </summary>
    Single = 1,
    /// <summary>
    /// The text has a continuous, blurred shadow.
    /// </summary>
    Continuous = 2,
}
