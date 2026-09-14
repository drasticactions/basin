namespace Basin.Capabilities;

public readonly record struct AreaPrompt(string AppId, string ParentWindow, bool Modal, IOutput? Output, bool PickPoint)
{
    public string DisplayName { get; init; } = "";

    public string IconPath { get; init; } = "";
}
