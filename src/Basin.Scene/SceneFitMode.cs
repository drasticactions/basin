namespace Basin.Scene;

/// <summary>
/// Names how <see cref="SceneFit"/> places content whose size differs from its target box.
/// </summary>
public enum SceneFitMode
{
    /// <summary>
    /// Scales the content up or down to the largest size that fits, keeping its aspect ratio.
    /// </summary>
    Fit,
    /// <summary>
    /// Centers the content at its own size. It scales down only when the content is larger than the target.
    /// </summary>
    Center,
    /// <summary>
    /// Like <see cref="Fit"/>, but an upscale rounds down to a whole factor.
    /// </summary>
    Integer,
    /// <summary>
    /// Scales the content to cover the whole target, keeping its aspect ratio, and clips the overflow.
    /// </summary>
    Fill,
    /// <summary>
    /// Scales each axis on its own so that the content fills the target exactly.
    /// </summary>
    Stretch,
}
