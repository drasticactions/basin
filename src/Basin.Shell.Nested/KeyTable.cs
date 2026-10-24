namespace Basin.Shell.Nested;

/// <summary>
/// The shell actions that are bound to chords, after the configured keys, the defaults and the host chords are resolved.
/// It is immutable. <see cref="NestedShell.Apply"/> replaces it as a whole.
/// </summary>
public sealed class KeyTable
{
    private readonly Dictionary<(ShellModifiers Modifiers, uint Code), string> _names;

    private KeyTable(IReadOnlyList<ShellKeyBinding> bindings)
    {
        Bindings = bindings;
        _names = bindings.ToDictionary(static binding => (binding.Modifiers, binding.Code), static binding => binding.Name);
    }

    /// <summary>
    /// Gets a table with no bindings. No chord matches it.
    /// </summary>
    public static KeyTable Empty { get; } = new([]);

    /// <summary>
    /// Gets the bindings in the order of <see cref="ShellKeyNames.All"/>.
    /// </summary>
    public IReadOnlyList<ShellKeyBinding> Bindings { get; }

    /// <summary>
    /// Finds the action bound to a chord. The held modifiers must equal the chord modifiers exactly.
    /// </summary>
    /// <param name="held">The modifiers held now.</param>
    /// <param name="code">The evdev code of the pressed key.</param>
    /// <returns>The action name, or <see langword="null"/> when no action uses the chord.</returns>
    public string? Match(ShellModifiers held, uint code) =>
        _names.TryGetValue((held, code), out var name) ? name : null;

    /// <summary>
    /// Builds the table from the default chords of <see cref="ShellKeyNames"/> and the configured keys.
    /// A chord that does not parse, that the host reserves, or that an earlier action uses is dropped with a logged warning.
    /// </summary>
    /// <param name="configured">The configured keys. Each one replaces the default chord of its action, and an unknown name is ignored.</param>
    /// <param name="reserved">The chords that the host binds. The host keeps each of them.</param>
    /// <returns>A new table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configured"/> or <paramref name="reserved"/> is <see langword="null"/>.</exception>
    public static KeyTable Build(IReadOnlyList<ShellKey> configured, IReadOnlyList<ShellChord> reserved)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(reserved);
        var log = NestedLog.Log;
        var chords = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in ShellKeyNames.All)
        {
            chords[name] = ShellKeyNames.DefaultChord(name);
        }

        foreach (var key in configured)
        {
            if (ShellKeyNames.IsKnown(key.Name))
            {
                var chord = key.Chord.Trim();
                chords[key.Name] = chord.Length == 0 ? null : chord;
            }
        }

        var host = new HashSet<(ShellModifiers, uint)>();
        foreach (var chord in reserved)
        {
            if (chord.Code != 0)
            {
                host.Add((chord.Modifiers, chord.Code));
            }
        }

        var owners = new Dictionary<(ShellModifiers, uint), string>();
        var bindings = new List<ShellKeyBinding>();
        foreach (var name in ShellKeyNames.All)
        {
            if (chords[name] is not { } chord)
            {
                continue;
            }

            if (ChordProblem(chord, out var modifiers, out var code) is { } problem)
            {
                log.Warn($"shell key {name}: {problem}");
                continue;
            }

            if (host.Contains((modifiers, code)))
            {
                log.Warn($"shell key {name} uses {chord}, which the host already binds, keeping the host's");
                continue;
            }

            if (owners.TryGetValue((modifiers, code), out var owner))
            {
                log.Warn($"shell key {owner} and {name} both use {chord}, keeping {owner}");
                continue;
            }

            owners[(modifiers, code)] = name;
            bindings.Add(new ShellKeyBinding(name, chord, modifiers, code));
        }

        return new KeyTable(bindings);
    }

    /// <summary>
    /// Parses chord text such as <c>Ctrl+Alt+Left</c>. Each token before the last must name a modifier, and the last token must name a key.
    /// </summary>
    /// <param name="chord">The chord text. Tokens are separated by <c>+</c>, and the case is ignored.</param>
    /// <param name="modifiers">The parsed modifiers, valid only when the method returns <see langword="null"/>.</param>
    /// <param name="code">The evdev key code, or zero when the key name is unknown.</param>
    /// <returns><see langword="null"/> when the chord is valid, or a message that says what is wrong.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chord"/> is <see langword="null"/>.</exception>
    public static string? ChordProblem(string chord, out ShellModifiers modifiers, out uint code)
    {
        ArgumentNullException.ThrowIfNull(chord);
        modifiers = ShellModifiers.None;
        code = 0;
        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return $"'{chord}' names no key";
        }

        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (ShellKeyCodes.ModifierNamed(tokens[i]) is { } modifier)
            {
                modifiers |= modifier;
            }
            else
            {
                return $"'{chord}' has no modifier named '{tokens[i]}'; the modifiers are shift, ctrl, alt and super";
            }
        }

        code = ShellKeyCodes.CodeFor(tokens[^1]);
        return code == 0 ? $"'{chord}' names no key" : null;
    }
}
