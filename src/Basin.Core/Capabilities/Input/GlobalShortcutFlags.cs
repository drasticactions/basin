namespace Basin.Capabilities;

/// <summary>
/// Options for a global shortcut. The values are bits, and a shortcut can combine them.
/// </summary>
[Flags]
public enum GlobalShortcutFlags
{
    /// <summary>No option is set.</summary>
    None = 0,
    /// <summary>The shortcut is removed after it fires once.</summary>
    OneShot = 1,
    /// <summary>The key event that triggers the shortcut also goes to the focused client.</summary>
    NoBlock = 2,
}
