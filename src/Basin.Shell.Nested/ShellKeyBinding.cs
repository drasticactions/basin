namespace Basin.Shell.Nested;

public sealed record ShellKeyBinding(string Name, string Chord, ShellModifiers Modifiers, uint Code);
