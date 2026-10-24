namespace Basin.Capabilities;

/// <summary>
/// Describes a request for the user to grant input devices to an application, for example in a remote desktop session.
/// </summary>
/// <param name="AppId">The app id of the requesting application. It is empty for a host application.</param>
/// <param name="ParentWindow">The portal parent-window handle string, for example <c>wayland:HANDLE</c>. It is empty when the request gave none.</param>
/// <param name="Modal">A value indicating whether the dialog must block its parent window.</param>
/// <param name="Requested">The device types that the application asked for.</param>
/// <param name="ClipboardRequested">A value indicating whether the application also asked for clipboard access.</param>
/// <param name="OfferPersist">A value indicating whether the dialog can offer to remember the grant.</param>
public readonly record struct DevicePrompt(
    string AppId,
    string ParentWindow,
    bool Modal,
    InputDeviceCapability Requested,
    bool ClipboardRequested,
    bool OfferPersist)
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
