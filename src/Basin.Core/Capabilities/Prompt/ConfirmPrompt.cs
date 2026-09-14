namespace Basin.Capabilities;

public readonly record struct ConfirmPrompt(string AppId, string ParentWindow, bool Modal, string Title, string Body)
{
    public string DisplayName { get; init; } = "";

    public string IconPath { get; init; } = "";
}
