using Basin.Config;
using Basin.Diagnostics;
using Tomlyn.Model;

namespace TabWm;

internal static class TabBindings
{
    private static readonly (string Chord, string Action)[] Defaults =
    [
        ("Super+T", "new-tab"),
        ("Super+W", "close-tab"),
        ("Super+Tab", "next-tab"),
        ("Super+Shift+Tab", "previous-tab"),
        ("Super+1", "tab-1"),
        ("Super+2", "tab-2"),
        ("Super+3", "tab-3"),
        ("Super+4", "tab-4"),
        ("Super+5", "tab-5"),
        ("Super+6", "tab-6"),
        ("Super+7", "tab-7"),
        ("Super+8", "tab-8"),
        ("Super+9", "last-tab"),
        ("Super+Space", "launcher"),
        ("Super+Shift+1", "move-to-tab-1"),
        ("Super+Shift+2", "move-to-tab-2"),
        ("Super+Shift+3", "move-to-tab-3"),
        ("Super+Shift+4", "move-to-tab-4"),
        ("Super+Shift+5", "move-to-tab-5"),
        ("Super+Shift+6", "move-to-tab-6"),
        ("Super+Shift+7", "move-to-tab-7"),
        ("Super+Shift+8", "move-to-tab-8"),
        ("Super+Shift+9", "move-to-tab-9"),
        ("Super+Shift+Prior", "move-to-previous-tab"),
        ("Super+Shift+Next", "move-to-next-tab"),
        ("Super+Shift+Left", "move-tab-left"),
        ("Super+Shift+Right", "move-tab-right"),
        ("Super+Q", "close-window"),
        ("Super+F", "fullscreen"),
        ("Super+M", "maximize"),
        ("Super+H", "minimize"),
        ("Super+Shift+M", "mute-tab"),
        ("Super+Escape", "lock"),
        ("Super+Alt+S", "screen-reader"),
        ("Super+Alt+8", "magnifier"),
        ("Super+Alt+equal", "magnifier-in"),
        ("Super+Alt+minus", "magnifier-out"),
        ("Super+Alt+C", "screen-curtain"),
    ];

    public static List<TabBinding> Build(TomlTable? keys, BasinLogger log)
    {
        var bindings = new List<TabBinding>();
        foreach (var (chord, action) in Defaults)
        {
            if (HotkeyParser.TryParseChord(chord, log, out var keysym, out var modifiers) && Parse(action) is { } parsed)
            {
                bindings.Add(new TabBinding(keysym, modifiers, parsed.Action, parsed.Argument, Chord: chord, Label: action));
            }
        }

        if (keys is null)
        {
            return Lowered(bindings);
        }

        foreach (var (chord, value) in keys)
        {
            if (HotkeyParser.Parse(chord, value, log, static name => Parse(name) is not null) is not { } hotkey)
            {
                continue;
            }

            var lowered = new Xkb.XkbKeysym(hotkey.Keysym).ToLower().Value;
            bindings.RemoveAll(b => new Xkb.XkbKeysym(b.Keysym).ToLower().Value == lowered && b.Modifiers == hotkey.ModifierMask);
            if (hotkey.Unbinds)
            {
                continue;
            }

            if (hotkey.Action is { } name && Parse(name) is { } named)
            {
                bindings.Add(new TabBinding(hotkey.Keysym, hotkey.ModifierMask, named.Action, named.Argument, Chord: chord, Label: name));
            }
            else if (hotkey.Command is { Length: > 0 } command)
            {
                bindings.Add(new TabBinding(hotkey.Keysym, hotkey.ModifierMask, TabAction.Exec, 0, command, chord, string.Join(' ', command)));
            }
        }

        return Lowered(bindings);
    }

    private static List<TabBinding> Lowered(List<TabBinding> bindings)
    {
        for (var i = 0; i < bindings.Count; i++)
        {
            bindings[i] = bindings[i] with { Keysym = new Xkb.XkbKeysym(bindings[i].Keysym).ToLower().Value };
        }

        return bindings;
    }

    public static (TabAction Action, int Argument)? Parse(string name)
    {
        switch (name)
        {
            case "new-tab": return (TabAction.NewTab, 0);
            case "close-tab": return (TabAction.CloseTab, 0);
            case "next-tab": return (TabAction.NextTab, 0);
            case "previous-tab": return (TabAction.PreviousTab, 0);
            case "last-tab": return (TabAction.LastTab, 0);
            case "launcher": return (TabAction.Launcher, 0);
            case "move-to-previous-tab": return (TabAction.MoveWindowToPreviousTab, 0);
            case "move-to-next-tab": return (TabAction.MoveWindowToNextTab, 0);
            case "move-tab-left": return (TabAction.MoveTabLeft, 0);
            case "move-tab-right": return (TabAction.MoveTabRight, 0);
            case "close-window": return (TabAction.CloseWindow, 0);
            case "fullscreen": return (TabAction.Fullscreen, 0);
            case "maximize": return (TabAction.Maximize, 0);
            case "minimize": return (TabAction.Minimize, 0);
            case "mute-tab": return (TabAction.MuteTab, 0);
            case "lock": return (TabAction.Lock, 0);
            case "screen-reader": return (TabAction.ScreenReader, 0);
            case "magnifier": return (TabAction.Magnifier, 0);
            case "magnifier-in": return (TabAction.MagnifierIn, 0);
            case "magnifier-out": return (TabAction.MagnifierOut, 0);
            case "screen-curtain": return (TabAction.ScreenCurtain, 0);
        }

        if (name.StartsWith("move-to-tab-", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan("move-to-tab-".Length), out var target) && target is >= 1 and <= 9)
        {
            return (TabAction.MoveWindowToTab, target);
        }

        if (name.StartsWith("tab-", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan("tab-".Length), out var index) && index is >= 1 and <= 9)
        {
            return (TabAction.SelectTab, index);
        }

        return null;
    }
}
