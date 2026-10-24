namespace Basin.Shell.Nested;

/// <summary>
/// A configured key for one shell action. It replaces the default chord of that action.
/// </summary>
/// <param name="Name">The action name. <see cref="KeyTable.Build"/> ignores a name that <see cref="ShellKeyNames.IsKnown"/> rejects.</param>
/// <param name="Chord">The chord text, for example <c>Ctrl+Alt+Left</c>. An empty or blank chord unbinds the action.</param>
public sealed record ShellKey(string Name, string Chord);
