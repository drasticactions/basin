namespace Basin.Shell.Nested;

/// <summary>
/// A key chord as modifiers plus one evdev key code. <see cref="KeyTable.Build"/> takes these for the chords that the host binds.
/// </summary>
/// <param name="Modifiers">The modifiers that must be held.</param>
/// <param name="Code">The evdev code of the key. Zero means no key, and <see cref="KeyTable.Build"/> ignores the chord.</param>
public readonly record struct ShellChord(ShellModifiers Modifiers, uint Code);
