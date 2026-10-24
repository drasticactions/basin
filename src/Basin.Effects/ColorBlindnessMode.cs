namespace Basin.Effects;

/// <summary>
/// Names the color vision defect that <see cref="ColorBlindnessStage"/> corrects for. The value is the shader uniform.
/// </summary>
public enum ColorBlindnessMode
{
    /// <summary>
    /// Corrects for a missing red response.
    /// </summary>
    Protanopia = 0,

    /// <summary>
    /// Corrects for a missing green response.
    /// </summary>
    Deuteranopia = 1,

    /// <summary>
    /// Corrects for a missing blue response.
    /// </summary>
    Tritanopia = 2,

    /// <summary>
    /// Draws the frame in grayscale.
    /// </summary>
    Monochrome = 3,
}
