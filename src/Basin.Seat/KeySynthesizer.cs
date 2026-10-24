using System.Text;
using Basin.Capabilities;
using Xkb;
using static Basin.Seat.SeatLog;

namespace Basin.Seat;

/// <summary>
/// Types text as key events into an <see cref="IKeySink"/>, with the modifier keys that each character needs in a keymap.
/// </summary>
public sealed class KeySynthesizer
{
    private readonly IKeySink _sink;
    private readonly Dictionary<uint, (uint Keycode, uint Modifiers, bool Found)> _cache = [];
    private XkbKeymap? _keymap;
    private uint _layout;
    private uint[]? _modifierKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeySynthesizer"/> class.
    /// </summary>
    /// <param name="sink">The sink that receives the key events. When it is an <see cref="IKeymapOverride"/>, characters outside the keymap can be typed too.</param>
    public KeySynthesizer(IKeySink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <summary>
    /// Gets or sets the keymap used to find keys. A change clears the lookup cache. The caller keeps ownership.
    /// </summary>
    public XkbKeymap? Keymap
    {
        get => _keymap;
        set
        {
            if (ReferenceEquals(_keymap, value))
            {
                return;
            }

            _keymap = value;
            Forget();
        }
    }

    /// <summary>
    /// Gets or sets the layout index in the keymap. A change clears the lookup cache.
    /// </summary>
    public uint Layout
    {
        get => _layout;
        set
        {
            if (_layout == value)
            {
                return;
            }

            _layout = value;
            Forget();
        }
    }

    /// <summary>
    /// Types text. When a character has no key, the whole text goes to <see cref="IKeySink.TryCommitText"/> first. Characters that still cannot be typed are dropped with a warning.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="timeMs">The event time in milliseconds for the key events.</param>
    /// <returns>The number of characters typed or committed.</returns>
    public int Type(ReadOnlySpan<char> text, uint timeMs)
    {
        _typer ??= new TextTyper(new Lookup(this), _sink as IKeymapOverride) { SkipUntypable = true };
        if (HasMissing(text) && _sink.TryCommitText(text))
        {
            var committed = 0;
            foreach (var _ in text.EnumerateRunes())
            {
                committed++;
            }

            return committed;
        }

        _target ??= new Target(_sink);
        var typed = _typer.Type(text, timeMs, _target, out var error);
        if (error is not null)
        {
            Log.Warn($"keyboard: {error}, the character is dropped");
        }

        return typed;
    }

    private TextTyper? _typer;
    private Target? _target;

    private bool HasMissing(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (!_typer!.CanType(rune))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Lookup(KeySynthesizer owner) : IKeymapLookup
    {
        public bool TryKeycodeForKeysym(uint keysym, out uint keycode, out uint modifiers) =>
            owner.TryResolve(keysym, out keycode, out modifiers);

        public uint KeysymForKeycode(uint keycode) => 0;
    }

    private sealed class Target(IKeySink sink) : ITextKeyTarget
    {
        public bool Key(uint timeMs, uint keycode, bool pressed)
        {
            sink.NotifyKey(timeMs, keycode, pressed);
            return true;
        }
    }

    /// <summary>
    /// Finds a key and a modifier mask that produce a keysym. Only masks whose modifiers a key in the keymap can hold are used. Results are cached.
    /// </summary>
    /// <param name="keysym">The keysym.</param>
    /// <param name="keycode">The Linux evdev keycode.</param>
    /// <param name="modifiers">The modifier mask to hold.</param>
    /// <returns><see langword="false"/> when there is no keymap or no key produces the keysym.</returns>
    public bool TryResolve(uint keysym, out uint keycode, out uint modifiers)
    {
        if (_cache.TryGetValue(keysym, out var cached))
        {
            keycode = cached.Keycode;
            modifiers = cached.Modifiers;
            return cached.Found;
        }

        var found = Resolve(keysym, out keycode, out modifiers);
        _cache[keysym] = (keycode, modifiers, found);
        return found;
    }

    private bool Resolve(uint keysym, out uint keycode, out uint modifiers)
    {
        keycode = 0;
        modifiers = 0;
        if (_keymap is not { } keymap)
        {
            return false;
        }

        for (var candidate = keymap.MinKeycode; candidate <= keymap.MaxKeycode; candidate++)
        {
            var levels = keymap.GetNumLevelsForKey(candidate, _layout);
            for (var level = 0u; level < levels; level++)
            {
                var syms = keymap.GetKeySymsByLevel(candidate, _layout, level);
                var matches = false;
                for (var i = 0; i < syms.Length && !matches; i++)
                {
                    matches = syms[i].Value == keysym;
                }

                if (!matches)
                {
                    continue;
                }

                var masks = keymap.GetModsForLevel(candidate, _layout, level);
                for (var i = 0; i < masks.Length; i++)
                {
                    if (CanHold(masks[i]))
                    {
                        keycode = candidate - 8;
                        modifiers = masks[i];
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool CanHold(uint mask)
    {
        for (var bit = 0; bit < 32 && mask >> bit != 0; bit++)
        {
            if ((mask & (1u << bit)) != 0 && ModifierKeyFor(bit) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private uint ModifierKeyFor(int bit)
    {
        if (_keymap is not { } keymap)
        {
            return 0;
        }

        if (_modifierKeys is null)
        {
            _modifierKeys = new uint[32];
            using var state = keymap.CreateState();
            for (var candidate = keymap.MinKeycode; candidate <= keymap.MaxKeycode; candidate++)
            {
                state.UpdateKey(candidate, XkbKeyDirection.Down);
                var depressed = state.SerializeMods(XkbStateComponent.ModsDepressed);
                state.UpdateKey(candidate, XkbKeyDirection.Up);
                for (var i = 0; i < 32 && depressed >> i != 0; i++)
                {
                    if ((depressed & (1u << i)) != 0 && _modifierKeys[i] == 0)
                    {
                        _modifierKeys[i] = candidate - 8;
                    }
                }
            }
        }

        return _modifierKeys[bit];
    }

    private void Forget()
    {
        _cache.Clear();
        _modifierKeys = null;
    }
}
