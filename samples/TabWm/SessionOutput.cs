namespace TabWm;

internal sealed class SessionOutput
{
    public string Connector { get; set; } = string.Empty;

    public int Selected { get; set; }

    public List<SessionTab> Tabs { get; set; } = [];
}
