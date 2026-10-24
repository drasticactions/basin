namespace Basin.Shell.Nested;

/// <summary>
/// The action that a click on a title bar runs. <see cref="ShellSettings"/> selects one per button.
/// </summary>
public enum TitlebarAction
{
    /// <summary>
    /// The click does nothing.
    /// </summary>
    None,
    /// <summary>
    /// Rolls the window up to its title bar, or rolls it down again.
    /// </summary>
    ToggleShade,
    /// <summary>
    /// Maximizes the window, or restores it.
    /// </summary>
    ToggleMaximize,
    /// <summary>
    /// Maximizes the window on the horizontal axis only, or restores it.
    /// </summary>
    ToggleMaximizeHorizontally,
    /// <summary>
    /// Maximizes the window on the vertical axis only, or restores it.
    /// </summary>
    ToggleMaximizeVertically,
    /// <summary>
    /// Minimizes the window.
    /// </summary>
    Minimize,
    /// <summary>
    /// Puts the window below the other windows in its layer.
    /// </summary>
    Lower,
    /// <summary>
    /// Opens the window menu at the click.
    /// </summary>
    Menu,
}
