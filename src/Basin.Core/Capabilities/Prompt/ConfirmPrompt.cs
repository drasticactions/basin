namespace Basin.Capabilities;

/// <summary>
/// Describes a yes-or-no question that a portal request asks the user before it continues.
/// </summary>
/// <param name="AppId">The app id of the requesting application. It is empty for a host application.</param>
/// <param name="ParentWindow">The portal parent-window handle string, for example <c>wayland:HANDLE</c>. It is empty when the request gave none.</param>
/// <param name="Modal">A value indicating whether the dialog must block its parent window.</param>
/// <param name="Title">The heading of the dialog.</param>
/// <param name="Body">The explanation text under the heading.</param>
public readonly record struct ConfirmPrompt(string AppId, string ParentWindow, bool Modal, string Title, string Body)
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
