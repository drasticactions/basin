namespace Basin.Capabilities;

/// <summary>
/// Holds the trigger that the user accepted for one global shortcut.
/// </summary>
/// <param name="Id">The shortcut id that the application registered.</param>
/// <param name="Trigger">The trigger in the freedesktop shortcuts grammar, for example <c>CTRL+SHIFT+r</c>.</param>
public readonly record struct ShortcutBinding(string Id, string Trigger);
