namespace Basin.Capabilities;

public readonly record struct ShortcutPrompt(string AppId, string ParentWindow, bool Modal, IReadOnlyList<ShortcutPromptRow> Shortcuts)
{
    public string DisplayName { get; init; } = "";

    public string IconPath { get; init; } = "";
}
