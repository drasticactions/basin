namespace Basin.Capabilities;

/// <summary>
/// Describes a request for the user to select a rectangle or a point on screen, for example for an interactive screenshot.
/// </summary>
/// <param name="AppId">The app id of the requesting application. It is empty for a host application.</param>
/// <param name="ParentWindow">The portal parent-window handle string, for example <c>wayland:HANDLE</c>. It is empty when the request gave none.</param>
/// <param name="Modal">A value indicating whether the dialog must block its parent window.</param>
/// <param name="Output">The output to show the picker on, or <see langword="null"/> to let the implementation choose.</param>
/// <param name="PickPoint">A value indicating whether the user picks a single point instead of a rectangle.</param>
public readonly record struct AreaPrompt(string AppId, string ParentWindow, bool Modal, IOutput? Output, bool PickPoint)
{
    /// <summary>
    /// Gets the human-readable application name that the dialog shows. It is empty when the request did not resolve one.
    /// </summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// Gets the path of the application icon that the dialog shows. It is empty when no icon is known.
    /// </summary>
    public string IconPath { get; init; } = "";
}
