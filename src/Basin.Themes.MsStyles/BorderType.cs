namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects the border shape of a border-fill background. The values are the uxtheme <c>BORDERTYPE</c> values.
/// </summary>
public enum BorderType
{
    /// <summary>
    /// The border is a rectangle.
    /// </summary>
    Rect = 0,
    /// <summary>
    /// The border is a rectangle with round corners.
    /// </summary>
    RoundRect = 1,
    /// <summary>
    /// The border is an ellipse.
    /// </summary>
    Ellipse = 2,
}
