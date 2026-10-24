namespace Basin.Capabilities;

/// <summary>
/// Receives the changes and the activations of the shortcuts in an <see cref="IGlobalShortcuts"/> registry.
/// </summary>
public interface IGlobalShortcutObserver
{
    /// <summary>Called after a shortcut is added to the registry.</summary>
    /// <param name="shortcut">The shortcut that was added.</param>
    void ShortcutRegistered(in GlobalShortcutInfo shortcut);

    /// <summary>Called after a shortcut is removed from the registry.</summary>
    /// <param name="shortcut">The shortcut that was removed.</param>
    void ShortcutRemoved(in GlobalShortcutInfo shortcut);

    /// <summary>Called when the trigger of a shortcut is pressed. The default implementation does nothing.</summary>
    /// <param name="shortcut">The shortcut that was pressed.</param>
    /// <param name="timestampMs">The time of the press, in milliseconds on a monotonic clock.</param>
    void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
    }

    /// <summary>Called when the trigger of a shortcut is released. The default implementation does nothing.</summary>
    /// <param name="shortcut">The shortcut that was released.</param>
    /// <param name="timestampMs">The time of the release, in milliseconds on a monotonic clock.</param>
    void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
    }
}
