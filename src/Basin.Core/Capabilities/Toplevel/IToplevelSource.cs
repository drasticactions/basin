namespace Basin.Capabilities;

public interface IToplevelSource
{
    int Enumerate(Span<ToplevelInfo> toplevels);

    bool TryGet(ulong localId, out ToplevelInfo info);

    bool Request(ulong localId, in ToplevelRequest request);

    void AddObserver(IToplevelObserver observer);

    void RemoveObserver(IToplevelObserver observer);
}
