namespace Basin.Capabilities;

public interface IToplevelStack
{
    int Enumerate(Span<ulong> toplevels);

    void AddObserver(IToplevelStackObserver observer);

    void RemoveObserver(IToplevelStackObserver observer);

    bool TryToplevelAt(double x, double y, out ulong toplevelId)
    {
        toplevelId = 0;
        return false;
    }
}
