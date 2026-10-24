namespace Basin.Shell.Nested;

/// <summary>
/// One shell action that <see cref="KeyTable.Build"/> bound to a chord after it removed conflicts.
/// </summary>
/// <param name="Name">The action name from <see cref="ShellKeyNames.All"/>.</param>
/// <param name="Chord">The chord text as configured, for example <c>Alt+Tab</c>.</param>
/// <param name="Modifiers">The parsed modifiers of the chord.</param>
/// <param name="Code">The parsed evdev key code of the chord.</param>
public sealed record ShellKeyBinding(string Name, string Chord, ShellModifiers Modifiers, uint Code);
