namespace Basin.Themes.MsStyles;

/// <summary>
/// Selects how a true-size image scales. The values are the uxtheme <c>TRUESIZESCALINGTYPE</c> values.
/// </summary>
public enum TrueSizeScalingType
{
    /// <summary>
    /// The image does not scale.
    /// </summary>
    None = 0,
    /// <summary>
    /// The image scales with the size of the destination.
    /// </summary>
    Size = 1,
    /// <summary>
    /// The image scales with the output DPI.
    /// </summary>
    Dpi = 2,
}
