namespace Basin.Capabilities;

/// <summary>
/// Describes one global shortcut that an application registered.
/// The pair of <paramref name="AppId"/> and <paramref name="Id"/> identifies the shortcut.
/// </summary>
/// <param name="AppId">The application that owns the shortcut.</param>
/// <param name="Id">The identifier of the shortcut, unique within its application.</param>
/// <param name="Description">Text that tells the user what the shortcut does.</param>
/// <param name="TriggerDescription">Text that tells the user which keys trigger the shortcut now.</param>
/// <param name="PreferredTrigger">The trigger that the application asks for, or an empty string when it asks for none.</param>
/// <param name="Flags">The options of the shortcut.</param>
public readonly record struct GlobalShortcutInfo(
    string AppId,
    string Id,
    string Description,
    string TriggerDescription,
    string PreferredTrigger = "",
    GlobalShortcutFlags Flags = GlobalShortcutFlags.None);
