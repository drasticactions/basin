namespace Basin.Capabilities;

/// <summary>
/// Describes one shortcut in a <see cref="ShortcutPrompt"/>.
/// </summary>
/// <param name="Id">The shortcut id that the application registered.</param>
/// <param name="Description">The human-readable text that the application gave for the shortcut.</param>
/// <param name="PreferredTrigger">The trigger that the application suggests. It is empty when it suggests none.</param>
/// <param name="PreferredTaken">A value indicating whether another shortcut already uses the preferred trigger.</param>
/// <param name="CurrentTrigger">The trigger that the shortcut has now. It is empty when it is not bound.</param>
public readonly record struct ShortcutPromptRow(string Id, string Description, string PreferredTrigger, bool PreferredTaken, string CurrentTrigger);
