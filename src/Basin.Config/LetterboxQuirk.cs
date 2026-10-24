namespace Basin.Config;

/// <summary>
/// The values of the <c>letterbox</c> quirk. They set how a window whose size differs from its configured size is shown.
/// </summary>
public enum LetterboxQuirk
{
    /// <summary>
    /// Scales the content uniformly to the largest size that fits and centers it.
    /// </summary>
    Fit,
    /// <summary>
    /// Keeps the content at 1:1 and centers it. Larger content is scaled down to fit.
    /// </summary>
    Center,
    /// <summary>
    /// Shows the content with no letterbox.
    /// </summary>
    Off,
    /// <summary>
    /// Fits the content with the scale floored to a whole number when it is above 1.
    /// </summary>
    Integer,
    /// <summary>
    /// Scales the content uniformly to cover the target and clips the overflow.
    /// </summary>
    Fill,
    /// <summary>
    /// Scales each axis on its own to the target size.
    /// </summary>
    Stretch,
}
