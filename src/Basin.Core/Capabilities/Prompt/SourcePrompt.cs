namespace Basin.Capabilities;

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
    public string DisplayName { get; init; } = "";

    public string IconPath { get; init; } = "";
}
