namespace Basin.Ipc;

/// <summary>
/// The data of the <c>shortcut/activated</c> event, which goes out when the trigger of a global shortcut goes down or up.
/// </summary>
/// <param name="AppId">The application that registered the shortcut.</param>
/// <param name="Id">The shortcut id, unique within its application.</param>
/// <param name="Pressed"><see langword="true"/> for a press of the trigger, <see langword="false"/> for its release.</param>
/// <param name="NoBlock"><see langword="true"/> when the key event also goes to the focused client.</param>
/// <param name="OneShot"><see langword="true"/> when the shortcut is removed after it fires once.</param>
public readonly record struct IpcShortcutActivated(string AppId, string Id, bool Pressed, bool NoBlock = false, bool OneShot = false);
