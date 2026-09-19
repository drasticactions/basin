using System.Text;
using Xkb;
using static Basin.Seat.SeatLog;

namespace Basin.Seat;

public sealed class KeySynthesizer
{
    private readonly IKeySink _sink;
    private readonly Dictionary<uint, (uint Keycode, uint Modifiers, bool Found)> _cache = [];
    private readonly List<uint> _held = new(4);
    private XkbKeymap? _keymap;
    private uint _layout;
    private uint[]? _modifierKeys;

    public KeySynthesizer(IKeySink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

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

    public int Type(ReadOnlySpan<char> text, uint timeMs)
    {
        var typed = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (TypeRune(rune, timeMs))
            {
                typed++;
            }
        }

        return typed;
    }

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

    private bool TypeRune(Rune rune, uint timeMs)
    {
        var keysym = XkbKeysym.FromUtf32((uint)rune.Value);
        if (keysym.IsNone || !TryResolve(keysym.Value, out var keycode, out var modifiers))
        {
            Log.Warn($"no key on the keymap produces U+{rune.Value:X4}; the character is dropped");
            return false;
        }

        _held.Clear();
        for (var bit = 0; bit < 32 && modifiers >> bit != 0; bit++)
        {
            if ((modifiers & (1u << bit)) == 0)
            {
                continue;
            }

            var modifierKey = ModifierKeyFor(bit);
            if (modifierKey == 0)
            {
                Log.Warn($"no key on the keymap sets modifier {bit} for U+{rune.Value:X4}; the character is dropped");
                return false;
            }

            _held.Add(modifierKey);
        }

        foreach (var held in _held)
        {
            _sink.NotifyKey(timeMs, held, pressed: true);
        }

        _sink.NotifyKey(timeMs, keycode, pressed: true);
        _sink.NotifyKey(timeMs, keycode, pressed: false);
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            _sink.NotifyKey(timeMs, _held[i], pressed: false);
        }

        return true;
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
