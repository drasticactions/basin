namespace Basin.Capabilities;

public interface IToplevelModel
{
    int Enumerate(Span<ToplevelInfo> toplevels);

    bool TryGet(ulong toplevelId, out ToplevelInfo info);

    void AddObserver(IToplevelObserver observer);

    void RemoveObserver(IToplevelObserver observer);

    bool Request(ulong toplevelId, in ToplevelRequest request);
}
