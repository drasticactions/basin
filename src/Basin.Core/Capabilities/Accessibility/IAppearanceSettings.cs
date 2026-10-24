namespace Basin.Capabilities;

/// <summary>
/// The session's appearance preferences that applications read through the Settings portal: color scheme, accent color, contrast and motion.
/// The compositor owns the values.
/// </summary>
public interface IAppearanceSettings
{
    /// <summary>
    /// Gets the current preferences.
    /// </summary>
    AppearancePreferences Current { get; }

    /// <summary>
    /// Occurs on the compositor loop after <see cref="Current"/> changes.
    /// </summary>
    event Action? Changed;
}
