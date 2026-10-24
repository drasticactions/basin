namespace TabWm;

internal sealed class SessionTab
{
    public string? Label { get; set; }

    public bool Muted { get; set; }

    public List<SessionApp> Apps { get; set; } = [];
}
