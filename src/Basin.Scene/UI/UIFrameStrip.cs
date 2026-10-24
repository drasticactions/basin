namespace Basin.Scene;

/// <summary>
/// One of the four strips of a <see cref="UIStripFrame"/>.
/// </summary>
public enum UIFrameStrip
{
    /// <summary>
    /// The strip above the content, with the titlebar and the top shadow margin.
    /// </summary>
    Title = 0,

    /// <summary>
    /// The strip left of the content.
    /// </summary>
    Left = 1,

    /// <summary>
    /// The strip right of the content.
    /// </summary>
    Right = 2,

    /// <summary>
    /// The strip below the content.
    /// </summary>
    Bottom = 3,
}
