namespace DeskbarWm;

internal sealed class Team(string key)
{
    public string Key { get; } = key;

    public List<ManagedWindow> Windows { get; } = [];

    public string? AppId { get; set; }

    public string? ResolvedName { get; set; }

    public bool? Expanded { get; set; }

    public string DisplayName =>
        ResolvedName
        ?? (Windows.Count > 0 ? Windows[0].Window.Title : null)
        ?? AppId
        ?? "?";
}
