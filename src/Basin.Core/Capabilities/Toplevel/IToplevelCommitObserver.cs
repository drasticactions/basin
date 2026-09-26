namespace Basin.Capabilities;

public interface IToplevelCommitObserver
{
    void OnToplevelCommitted(ulong toplevelId, in Box damage);
}
