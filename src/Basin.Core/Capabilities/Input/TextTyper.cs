using System.Text;

namespace Basin.Capabilities;

/// <summary>
/// Types a string as key presses through the active keymap.
/// It holds the needed modifiers for each character, and it can map spare keycodes for characters that the layout does not have.
/// </summary>
public sealed class TextTyper
{
    /// <summary>
    /// The evdev keycodes that <see cref="IKeymapOverride"/> can remap to type a missing character. One batch can use each keycode once.
    /// </summary>
    public static readonly uint[] SpareKeycodes = [183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194];

    private static readonly (uint Mask, uint Keysym)[] ModifierKeysyms =
    [
        (1u << 0, 0xffe1),
        (1u << 2, 0xffe3),
        (1u << 3, 0xffe9),
        (1u << 7, 0xfe03),
    ];

    private readonly IKeymapLookup _lookup;
    private readonly IKeymapOverride? _override;
    private readonly List<(uint Keycode, uint Mask, int Spare)> _plan = [];
    private readonly List<(uint Keycode, uint Keysym)> _spares = new(12);
    private readonly List<uint> _held = new(4);

    /// <summary>
    /// Initializes a new instance of the <see cref="TextTyper"/> class.
    /// </summary>
    /// <param name="lookup">The lookup that finds the key and the modifiers for each keysym.</param>
    /// <param name="keymapOverride">The override for characters that the keymap does not have, or <see langword="null"/> to type only what the keymap has.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lookup"/> is <see langword="null"/>.</exception>
    public TextTyper(IKeymapLookup lookup, IKeymapOverride? keymapOverride)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        _lookup = lookup;
        _override = keymapOverride;
    }

    /// <summary>
    /// Gets the XKB keysym for a character. Control characters map to their function keysyms, for example a newline to Return.
    /// </summary>
    /// <param name="rune">The character.</param>
    /// <returns>The Latin-1 keysym, a function keysym, or the Unicode keysym <c>0x01000000</c> plus the code point.</returns>
    public static uint KeysymOf(Rune rune) => rune.Value switch
    {
        '\n' or '\r' => 0xff0du,
        '\t' => 0xff09u,
        '\b' => 0xff08u,
        0x1b => 0xff1bu,
        >= 0x20 and <= 0x7e or >= 0xa0 and <= 0xff => (uint)rune.Value,
        _ => 0x01000000u | (uint)rune.Value,
    };

    /// <summary>
    /// Tells whether the active keymap can type a character with Shift, Control, Alt and Level 3 only. It does not consider spare keycodes.
    /// </summary>
    /// <param name="rune">The character.</param>
    /// <returns><see langword="true"/> when a key and its modifiers produce the character.</returns>
    public bool CanType(Rune rune) => Resolve(KeysymOf(rune), out _, out _);

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Type"/> skips a character that it cannot type instead of stopping there.
    /// It applies only without an <see cref="IKeymapOverride"/>.
    /// </summary>
    public bool SkipUntypable { get; set; }

    /// <summary>
    /// Types text as key presses and releases. It holds the modifiers around each key and releases them after it.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="timeMs">The event time in milliseconds for all key events.</param>
    /// <param name="target">The target that receives the key events.</param>
    /// <param name="error">A message for the first character that the keymap cannot type, or <see langword="null"/>.</param>
    /// <returns>The number of characters whose key events the target accepted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public int Type(ReadOnlySpan<char> text, uint timeMs, ITextKeyTarget target, out string? error)
    {
        ArgumentNullException.ThrowIfNull(target);
        error = null;
        var typed = 0;
        var start = 0;
        while (start < text.Length)
        {
            var planned = Plan(text, start, out var end, out var problem);
            error ??= problem;
            if (!planned)
            {
                return typed;
            }

            typed += Emit(timeMs, target);
            start = end;
        }

        return typed;
    }

    private bool Plan(ReadOnlySpan<char> text, int start, out int end, out string? error)
    {
        _plan.Clear();
        _spares.Clear();
        error = null;
        var index = start;
        var position = 0;
        foreach (var rune in text[start..].EnumerateRunes())
        {
            var keysym = KeysymOf(rune);
            if (Resolve(keysym, out var keycode, out var mask))
            {
                _plan.Add((keycode, mask, -1));
            }
            else
            {
                var spare = SpareOf(keysym);
                if (spare < 0)
                {
                    if (_override is null)
                    {
                        error ??= $"the keymap cannot type '{rune}' (U+{rune.Value:X4}) at position {start + position}";
                        if (!SkipUntypable)
                        {
                            end = index;
                            return false;
                        }

                        index += rune.Utf16SequenceLength;
                        position++;
                        continue;
                    }

                    if (_spares.Count == SpareKeycodes.Length)
                    {
                        break;
                    }

                    spare = _spares.Count;
                    _spares.Add((SpareKeycodes[spare], keysym));
                }

                _plan.Add((SpareKeycodes[spare], 0, spare));
            }

            index += rune.Utf16SequenceLength;
            position++;
        }

        end = index;
        return true;
    }

    private int SpareOf(uint keysym)
    {
        for (var i = 0; i < _spares.Count; i++)
        {
            if (_spares[i].Keysym == keysym)
            {
                return i;
            }
        }

        return -1;
    }

    private int Emit(uint timeMs, ITextKeyTarget target)
    {
        IDisposable? scope = null;
        if (_spares.Count > 0)
        {
            scope = _override!.OverrideKeysyms(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_spares));
        }

        var typed = 0;
        try
        {
            foreach (var (keycode, mask, spare) in _plan)
            {
                if (spare >= 0 && scope is null)
                {
                    continue;
                }

                _held.Clear();
                foreach (var (bit, keysym) in ModifierKeysyms)
                {
                    if ((mask & bit) != 0 && _lookup.TryKeycodeForKeysym(keysym, out var modifier, out _))
                    {
                        _held.Add(modifier);
                    }
                }

                var ok = true;
                foreach (var held in _held)
                {
                    ok &= target.Key(timeMs, held, true);
                }

                ok &= target.Key(timeMs, keycode, true);
                ok &= target.Key(timeMs, keycode, false);
                for (var i = _held.Count - 1; i >= 0; i--)
                {
                    ok &= target.Key(timeMs, _held[i], false);
                }

                typed += ok ? 1 : 0;
            }
        }
        finally
        {
            scope?.Dispose();
        }

        return typed;
    }

    private bool Resolve(uint keysym, out uint keycode, out uint mask)
    {
        if (!_lookup.TryKeycodeForKeysym(keysym, out keycode, out mask))
        {
            return false;
        }

        foreach (var (bit, modifierKeysym) in ModifierKeysyms)
        {
            if ((mask & bit) != 0 && !_lookup.TryKeycodeForKeysym(modifierKeysym, out _, out _))
            {
                return false;
            }

            mask &= ~bit;
        }

        if (mask != 0)
        {
            return false;
        }

        _ = _lookup.TryKeycodeForKeysym(keysym, out keycode, out mask);
        return true;
    }
}
