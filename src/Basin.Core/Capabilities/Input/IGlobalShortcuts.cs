namespace Basin.Capabilities;

public interface IGlobalShortcuts
{
    bool TryRegister(in GlobalShortcutInfo shortcut);

    void Unregister(string appId, string id);

    int Enumerate(Span<GlobalShortcutInfo> shortcuts);

    bool Trigger(string appId, string id, bool pressed, ulong timestampMs);

    void AddObserver(IGlobalShortcutObserver observer);

    void RemoveObserver(IGlobalShortcutObserver observer);
}
