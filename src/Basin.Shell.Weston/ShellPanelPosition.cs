namespace Basin.Shell.Weston;

/// <summary>
/// The screen edge that weston's panels sit on. The values are the wire values of weston's <c>panel_position</c> enum.
/// </summary>
public enum ShellPanelPosition
{
    /// <summary>
    /// The panel sits on the top edge.
    /// </summary>
    Top = 0,

    /// <summary>
    /// The panel sits on the bottom edge.
    /// </summary>
    Bottom = 1,

    /// <summary>
    /// The panel sits on the left edge.
    /// </summary>
    Left = 2,

    /// <summary>
    /// The panel sits on the right edge.
    /// </summary>
    Right = 3,
}
