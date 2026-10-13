using Basin.Config;
using Basin.Diagnostics;

namespace LunaWm;

internal sealed class LunaBindings
{
    public static readonly string[] Actions =
    [
        "card_view", "launcher", "quick_launch", "maximize", "back", "next", "previous",
        "home", "dashboard", "lock", "close", "keyboard",
    ];

    private static readonly (string Chord, string Action)[] Defaults =
    [
        ("Super", "card_view"),
        ("Super+space", "launcher"),
        ("Super+d", "quick_launch"),
        ("Super+Up", "maximize"),
        ("Super+BackSpace", "back"),
        ("Super+Right", "next"),
        ("Super+Left", "previous"),
        ("Super+Home", "home"),
        ("Super+n", "dashboard"),
        ("Super+l", "lock"),
        ("Super+q", "close"),
        ("Super+k", "keyboard"),
    ];

    private readonly List<Hotkey> _bindings = [];

    public LunaBindings(IReadOnlyDictionary<string, object>? table, BasinLogger log)
    {
        var given = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (chord, action) in Defaults)
        {
            given[chord] = action;
        }

        if (table is not null)
        {
            foreach (var (chord, value) in table)
            {
                given[chord] = value;
            }
        }

        foreach (var (chord, value) in given)
        {
            if (chord is "Super" or "super")
            {
                BareSuper = value is string bare && IsAction(bare) ? bare : null;
                continue;
            }

            if (HotkeyParser.Parse(chord, value, log, IsAction) is { } hotkey && !hotkey.Unbinds)
            {
                _bindings.Add(hotkey with { Keysym = new Xkb.XkbKeysym(hotkey.Keysym).ToLower().Value });
            }
        }
    }

    public string? BareSuper { get; }

    public int Count => _bindings.Count + (BareSuper is null ? 0 : 1);

    public IReadOnlyList<Hotkey> All => _bindings;

    public static bool IsAction(string name) => Array.IndexOf(Actions, name) >= 0;

    public Hotkey? Match(uint keysym, Modifiers held, bool focusedFullscreen = false)
    {
        var lower = new Xkb.XkbKeysym(keysym).ToLower().Value;
        foreach (var hotkey in _bindings)
        {
            if (hotkey.Keysym == lower && hotkey.ModifierMask == held && hotkey.AppliesTo(focusedFullscreen))
            {
                return hotkey;
            }
        }

        return null;
    }
}
