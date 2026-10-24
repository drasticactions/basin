namespace TabWm;

internal sealed class SessionFile
{
    public int Version { get; set; } = 1;

    public List<SessionOutput> Outputs { get; set; } = [];
}
