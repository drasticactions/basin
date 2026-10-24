namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects the point from which a part offset is measured. The values are the uxtheme <c>OFFSETTYPE</c> values.
/// </summary>
public enum OffsetType
{
    /// <summary>
    /// The offset is from the top-left corner.
    /// </summary>
    TopLeft = 0,
    /// <summary>
    /// The offset is from the top-right corner.
    /// </summary>
    TopRight = 1,
    /// <summary>
    /// The offset is from the middle of the top edge.
    /// </summary>
    TopMiddle = 2,
    /// <summary>
    /// The offset is from the bottom-left corner.
    /// </summary>
    BottomLeft = 3,
    /// <summary>
    /// The offset is from the bottom-right corner.
    /// </summary>
    BottomRight = 4,
    /// <summary>
    /// The offset is from the middle of the bottom edge.
    /// </summary>
    BottomMiddle = 5,
    /// <summary>
    /// The offset is from the middle of the left edge.
    /// </summary>
    MiddleLeft = 6,
    /// <summary>
    /// The offset is from the middle of the right edge.
    /// </summary>
    MiddleRight = 7,
    /// <summary>
    /// The offset is from the left of the caption text.
    /// </summary>
    LeftOfCaption = 8,
    /// <summary>
    /// The offset is from the right of the caption text.
    /// </summary>
    RightOfCaption = 9,
    /// <summary>
    /// The offset is from the left of the last caption button.
    /// </summary>
    LeftOfLastButton = 10,
    /// <summary>
    /// The offset is from the right of the last caption button.
    /// </summary>
    RightOfLastButton = 11,
    /// <summary>
    /// The offset is from above the last caption button.
    /// </summary>
    AboveLastButton = 12,
    /// <summary>
    /// The offset is from below the last caption button.
    /// </summary>
    BelowLastButton = 13,
}
