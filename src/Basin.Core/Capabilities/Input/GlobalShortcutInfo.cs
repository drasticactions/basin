namespace Basin.Capabilities;

public readonly record struct GlobalShortcutInfo(
    string AppId,
    string Id,
    string Description,
    string TriggerDescription,
    string PreferredTrigger = "");
