using Basin.Cli;
using System.Text.RegularExpressions;
using Basin.WindowManager;
using Tomlyn;
using Tomlyn.Model;

using Basin.Diagnostics;

namespace RetroWm;

internal sealed class Config
{
    public Modifiers MainModifier { get; private set; } = Modifiers.Alt;

    public DecorationPreference Decorations { get; private set; } = DecorationPreference.ForceSsd;

    public string[] TerminalCommand { get; private set; } = ["foot"];

    public IReadOnlyList<Rule> Rules { get; private set; } = [];

    public IReadOnlyList<HotkeyBinding> Hotkeys { get; private set; } = [];

    public static Config Load(bool skipFile, BasinLogger log)
    {
        var config = new Config();
        Theme.Reset();
        if (skipFile)
        {
            return config;
        }

        var path = TomlConfig.DefaultPath("retro-wm");
        if (TomlConfig.Read(path, log) is not { } table)
        {
            return config;
        }

        if (table.TryGetValue("main_modifier", out var modifier) && modifier is string modifierName)
        {
            config.MainModifier = modifierName.ToLowerInvariant() switch
            {
                "super" or "logo" or "win" or "mod4" => Modifiers.Super,
                "ctrl" or "control" => Modifiers.Ctrl,
                _ => Modifiers.Alt,
            };
        }

        config.TerminalCommand = Command(table, "terminal_cmd") ?? config.TerminalCommand;

        if (table.TryGetValue("decorations", out var decorations) && decorations is string mode)
        {
            config.Decorations = mode.ToLowerInvariant() switch
            {
                "prefer-ssd" or "prefer" => DecorationPreference.PreferSsd,
                "csd" or "none" => DecorationPreference.Csd,
                _ => DecorationPreference.ForceSsd,
            };
        }

        if (table.TryGetValue("ui", out var ui) && ui is TomlTable uiTable)
        {
            Theme.Apply(uiTable);
        }

        if (table.TryGetValue("rules", out var rules) && rules is TomlTableArray ruleTables)
        {
            config.Rules = [.. ruleTables.Select(rule => ParseRule(rule, log)).OfType<Rule>()];
        }

        if (table.TryGetValue("hotkeys", out var hotkeys) && hotkeys is TomlTable hotkeyTable)
        {
            var parsed = new List<HotkeyBinding>();
            foreach (var (chord, value) in hotkeyTable)
            {
                if (ParseHotkey(chord, value, log) is { } hotkey)
                {
                    parsed.Add(hotkey);
                }
            }

            config.Hotkeys = parsed;
        }

        return config;
    }

    public static WmAction? ActionFromName(string name)
    {
        if (name.StartsWith("send-workspace-", StringComparison.Ordinal)
            && int.TryParse(name["send-workspace-".Length..], out var sendIndex)
            && sendIndex is >= 1 and <= 9)
        {
            return WmAction.SendWorkspace1 + (sendIndex - 1);
        }

        if (name.StartsWith("workspace-", StringComparison.Ordinal)
            && int.TryParse(name["workspace-".Length..], out var switchIndex)
            && switchIndex is >= 1 and <= 9)
        {
            return WmAction.Workspace1 + (switchIndex - 1);
        }

        return name switch
        {
        "cycle" => WmAction.CycleForward,
        "cycle-back" => WmAction.CycleBackward,
        "menu" => WmAction.OpenMenu,
        "zoom" => WmAction.ZoomToggle,
        "iconize" => WmAction.Iconize,
        "close" => WmAction.Close,
        "spawn-terminal" => WmAction.SpawnTerminal,
        "send-left" => WmAction.SendLeft,
        "send-right" => WmAction.SendRight,
        "send-up" => WmAction.SendUp,
        "send-down" => WmAction.SendDown,
        "focus-left" => WmAction.FocusLeft,
        "focus-right" => WmAction.FocusRight,
        "focus-up" => WmAction.FocusUp,
        "focus-down" => WmAction.FocusDown,
        "move-left" => WmAction.ArrangeLeft,
        "move-right" => WmAction.ArrangeRight,
        "move-up" => WmAction.ArrangeUp,
        "move-down" => WmAction.ArrangeDown,
        "size-left" => WmAction.NudgeLeft,
        "size-right" => WmAction.NudgeRight,
        "size-up" => WmAction.NudgeUp,
        "size-down" => WmAction.NudgeDown,
        "move-mode" => WmAction.EnterMoveMode,
        "size-mode" => WmAction.EnterSizeMode,
        "restore" => WmAction.RestoreLast,
        "toggle-dock" => WmAction.ToggleDock,
        "exit" => WmAction.ExitSession,
            _ => null,
        };
    }

    private static string[]? Command(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return null;
        }

        var parts = value switch
        {
            string text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            TomlArray array => [.. array.OfType<string>().Select(static part => part.Trim()).Where(static part => part.Length > 0)],
            _ => Array.Empty<string>(),
        };
        return parts.Length > 0 ? parts : null;
    }

    private static Rule? ParseRule(TomlTable table, BasinLogger log)
    {
        static string[]? Strings(TomlTable table, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (table.TryGetValue(key, out var value))
                {
                    var parsed = value switch
                    {
                        string text => (string[])[text],
                        TomlArray array => [.. array.OfType<string>()],
                        _ => null,
                    };
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                }
            }

            return null;
        }

        Regex? Pattern(TomlTable table, string key)
        {
            if (table.TryGetValue(key, out var value) && value is string pattern)
            {
                try
                {
                    return new Regex(pattern);
                }
                catch (ArgumentException error)
                {
                    log.Warn($"rule pattern '{pattern}' is invalid: {error.Message}");
                }
            }

            return null;
        }

        bool? requireCsdOnly = null;
        bool? requireNoParent = null;
        foreach (var prop in Strings(table, "match_props") ?? [])
        {
            switch (prop)
            {
                case "csd_only":
                    requireCsdOnly = true;
                    break;
                case "toplevel":
                    requireNoParent = true;
                    break;
            }
        }

        return new Rule
        {
            AppIds = Strings(table, "match_app_id", "app_id"),
            AppIdPrefixes = Strings(table, "match_app_id_prefix"),
            Titles = Strings(table, "title"),
            AppIdRegex = Pattern(table, "app_id_regex"),
            TitleRegex = Pattern(table, "title_regex"),
            RequireCsdOnly = requireCsdOnly,
            RequireNoParent = requireNoParent,
            ForceSsd = table.TryGetValue("force_ssd", out var force) && force is true,
            SwallowTop = table.TryGetValue("swallow_top", out var swallow) && swallow is long pixels
                ? (int)pixels
                : null,
        };
    }

    private static HotkeyBinding? ParseHotkey(string chord, object value, BasinLogger log)
    {
        WmAction? action = null;
        string[]? command = null;
        if (value is string text && ActionFromName(text) is { } named)
        {
            action = named;
        }
        else
        {
            command = value switch
            {
                string commandText => commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                TomlArray array => [.. array.OfType<string>().Select(static part => part.Trim()).Where(static part => part.Length > 0)],
                _ => Array.Empty<string>(),
            };
            if (command.Length == 0)
            {
                log.Warn($"hotkey '{chord}' names no action and no command, skipping");
                return null;
            }
        }

        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        var modifiers = Modifiers.None;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "shift":
                    modifiers |= Modifiers.Shift;
                    break;
                case "ctrl" or "control":
                    modifiers |= Modifiers.Ctrl;
                    break;
                case "alt" or "mod1":
                    modifiers |= Modifiers.Alt;
                    break;
                case "super" or "logo" or "win" or "mod4":
                    modifiers |= Modifiers.Super;
                    break;
                case "mod3":
                    modifiers |= Modifiers.Mod3;
                    break;
                case "mod5":
                    modifiers |= Modifiers.Mod5;
                    break;
                default:
                    log.Warn($"unknown modifier '{(tokens[i])}' in hotkey '{chord}', skipping");
                    return null;
            }
        }

        var keysym = Keysym.FromName(tokens[^1]);
        if (keysym == Keysym.NoSymbol)
        {
            log.Warn($"unknown keysym '{(tokens[^1])}' in hotkey '{chord}', skipping");
            return null;
        }

        return new HotkeyBinding(keysym, modifiers, action, command);
    }
}
