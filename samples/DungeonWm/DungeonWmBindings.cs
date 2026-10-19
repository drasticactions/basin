using Basin.Config;
using Basin.Diagnostics;

namespace DungeonWm;

internal sealed class DungeonWmBindings
{
    public static readonly string[] Actions = ["leave-takeover", "next-room", "previous-room", "takeover", "close", "throw-back", "look-reset", "use", "release", "rotate", "square", "inspector"];

    private readonly List<Hotkey> _bindings = [];

    public DungeonWmBindings(IReadOnlyDictionary<string, object> table, BasinLogger log)
    {
        foreach (var (chord, value) in table)
        {
            if (ToHotkey(chord, value, log) is { } hotkey)
            {
                _bindings.Add(hotkey with { Keysym = new Xkb.XkbKeysym(hotkey.Keysym).ToLower().Value });
            }
        }

        Implicit('e', Modifiers.None, "use");
        Implicit('r', Modifiers.None, "rotate");
        Implicit('r', Modifiers.Shift, "square");
    }

    public int Count => _bindings.Count;

    private void Implicit(char key, Modifiers mask, string action)
    {
        var keysym = new Xkb.XkbKeysym(key).Value;
        if (!_bindings.Exists(binding => binding.Action == action) && Match(keysym, mask) is null)
        {
            _bindings.Add(new Hotkey(keysym, mask, action, null));
        }
    }

    public static bool IsAction(string name) =>
        Array.IndexOf(Actions, name) >= 0 || SpawnCommand(name) is not null;

    public static bool Accepts(object value) => value switch
    {
        string name => IsAction(name),
        Tomlyn.Model.TomlArray array => array.Count > 0 && array.All(part => part is string { Length: > 0 }),
        Tomlyn.Model.TomlTable table => table.TryGetValue("action", out var action) && action is not null && Accepts(action),
        _ => false,
    };

    public Hotkey? Match(uint keysym, Modifiers held, bool takeover = false)
    {
        foreach (var hotkey in _bindings)
        {
            if (hotkey.Keysym == keysym && hotkey.ModifierMask == held && hotkey.AppliesTo(takeover))
            {
                return hotkey;
            }
        }

        return null;
    }

    public static Modifiers Held(Basin.Seat.SeatKeyboard keyboard) => HeldModifiers.From(keyboard.State);

    private static Hotkey? ToHotkey(string chord, object value, BasinLogger log)
    {
        if (value is Tomlyn.Model.TomlTable table && table.TryGetValue("action", out var inner) && inner is string innerText
            && SpawnCommand(innerText) is { } tableCommand)
        {
            return HotkeyParser.Parse(chord, table, log, static _ => true) is { } scoped
                ? new Hotkey(scoped.Keysym, scoped.ModifierMask, null, ["/bin/sh", "-c", tableCommand]) { When = scoped.When }
                : null;
        }

        if (value is string text && SpawnCommand(text) is { } command)
        {
            return HotkeyParser.TryParseChord(chord, log, out var keysym, out var mask)
                ? new Hotkey(keysym, mask, null, ["/bin/sh", "-c", command])
                : null;
        }

        return HotkeyParser.Parse(chord, value, log, static name => Array.IndexOf(Actions, name) >= 0);
    }

    private static string? SpawnCommand(string text) =>
        text.StartsWith("spawn ", StringComparison.Ordinal) && text.AsSpan(6).Trim().Length > 0 ? text[6..].Trim() : null;
}
