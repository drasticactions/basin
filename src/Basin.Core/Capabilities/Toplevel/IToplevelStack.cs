namespace Basin.Capabilities;

public interface IToplevelStack
{
    int Enumerate(Span<ulong> toplevels);

    void AddObserver(IToplevelStackObserver observer);

    void RemoveObserver(IToplevelStackObserver observer);
}
