namespace Basin.Capabilities;

/// <summary>
/// Describes a request for the user to choose screencast sources.
/// </summary>
/// <param name="AppId">The app id of the requesting application. It is empty for a host application.</param>
/// <param name="ParentWindow">The portal parent-window handle string, for example <c>wayland:HANDLE</c>. It is empty when the request gave none.</param>
/// <param name="Modal">A value indicating whether the dialog must block its parent window.</param>
/// <param name="Kinds">The source kinds that the application accepts.</param>
/// <param name="Multiple">A value indicating whether the user can choose more than one source.</param>
/// <param name="OfferPersist">A value indicating whether the dialog can offer to remember the choice.</param>
/// <param name="Outputs">A snapshot of the outputs that the picker can offer.</param>
/// <param name="Toplevels">A snapshot of the windows that the picker can offer.</param>
public readonly record struct SourcePrompt(
    string AppId,
    string ParentWindow,
    bool Modal,
    PromptSourceKinds Kinds,
    bool Multiple,
    bool OfferPersist,
    IReadOnlyList<PromptOutput> Outputs,
    IReadOnlyList<PromptToplevel> Toplevels)
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
