namespace Basin.Scene;

/// <summary>
/// The user setting that <see cref="SceneFit.Needed"/> turns into a <see cref="SceneFitMode"/>.
/// </summary>
public enum SceneFitPolicy
{
    /// <summary>
    /// Uses <see cref="SceneFitMode.Fit"/> for fullscreen windows and <see cref="SceneFitMode.Center"/> for other constrained states.
    /// </summary>
    Auto,
    /// <summary>
    /// Always uses <see cref="SceneFitMode.Fit"/>.
    /// </summary>
    Fit,
    /// <summary>
    /// Always uses <see cref="SceneFitMode.Center"/>.
    /// </summary>
    Center,
    /// <summary>
    /// Never fits. The content shows at its own size and position.
    /// </summary>
    Off,
    /// <summary>
    /// Always uses <see cref="SceneFitMode.Integer"/>.
    /// </summary>
    Integer,
    /// <summary>
    /// Always uses <see cref="SceneFitMode.Fill"/>.
    /// </summary>
    Fill,
    /// <summary>
    /// Always uses <see cref="SceneFitMode.Stretch"/>.
    /// </summary>
    Stretch,
}
