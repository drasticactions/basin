namespace Basin.Capabilities;

public interface IGlobalShortcutObserver
{
    void ShortcutRegistered(in GlobalShortcutInfo shortcut);

    void ShortcutRemoved(in GlobalShortcutInfo shortcut);

    void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
    }

    void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
    }
}
