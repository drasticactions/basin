namespace Basin.Config;

/// <summary>
/// The states of the focused window in which a binding applies.
/// </summary>
public enum HotkeyScope
{
    /// <summary>
    /// The binding applies in every state.
    /// </summary>
    Always,
    /// <summary>
    /// The binding applies only when the focused window is not fullscreen.
    /// </summary>
    Windowed,
    /// <summary>
    /// The binding applies only when the focused window is fullscreen.
    /// </summary>
    Fullscreen,
}
