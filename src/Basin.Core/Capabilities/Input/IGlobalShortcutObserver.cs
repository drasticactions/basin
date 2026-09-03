namespace Basin.Capabilities;

public interface IGlobalShortcutObserver
{
    void ShortcutRegistered(in GlobalShortcutInfo shortcut);

    void ShortcutRemoved(in GlobalShortcutInfo shortcut);
}
