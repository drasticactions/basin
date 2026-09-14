namespace Basin.Capabilities;

public readonly record struct ShortcutPromptRow(string Id, string Description, string PreferredTrigger, bool PreferredTaken, string CurrentTrigger);
