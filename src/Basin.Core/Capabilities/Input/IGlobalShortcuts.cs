namespace Basin.Capabilities;

public interface IGlobalShortcuts
{
    bool TryRegister(in GlobalShortcutInfo shortcut);

    void Unregister(string appId, string id);

    int Enumerate(Span<GlobalShortcutInfo> shortcuts);

    void AddObserver(IGlobalShortcutObserver observer);

    void RemoveObserver(IGlobalShortcutObserver observer);
}
