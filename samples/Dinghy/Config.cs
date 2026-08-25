using Basin.Cli;
using System.Text.RegularExpressions;
using Basin.WindowManager;
using Tomlyn;
using Tomlyn.Model;

using Basin.Diagnostics;

namespace Dinghy;

internal sealed class Config
{
    public Modifiers MainModifier { get; private set; } = Modifiers.Super;

    public string[] TerminalCommand { get; private set; } = ["foot"];

    public string[] LauncherCommand { get; private set; } = ["fuzzel"];

    public string[] LockCommand { get; private set; } = ["swaylock"];

    public bool DesktopWallpaper { get; private set; } = true;

    public IReadOnlyList<Rule> Rules { get; private set; } = [];

    public IReadOnlyList<Hotkey> Hotkeys { get; private set; } = [];

    public static Config Load(bool skipFile, BasinLogger log)
    {
        var config = new Config();
        Theme.Reset();
        if (skipFile)
        {
            return config;
        }

        var path = TomlConfig.DefaultPath("dinghy");
        if (TomlConfig.Read(path, log) is not { } table)
        {
            return config;
        }

        if (table.TryGetValue("main_modifier", out var modifier) && modifier is string modifierName)
        {
            config.MainModifier = modifierName.ToLowerInvariant() switch
            {
                "alt" => Modifiers.Alt,
                _ => Modifiers.Super,
            };
        }

        config.TerminalCommand = Command(table, "terminal_cmd") ?? config.TerminalCommand;
        config.LauncherCommand = Command(table, "launcher_cmd") ?? config.LauncherCommand;
        config.LockCommand = Command(table, "lock_cmd") ?? config.LockCommand;

        if (table.TryGetValue("desktop_wallpaper", out var wallpaper) && wallpaper is bool drawWallpaper)
        {
            config.DesktopWallpaper = drawWallpaper;
        }

        if (table.TryGetValue("ui", out var ui) && ui is TomlTable uiTable)
        {
            Theme.Apply(uiTable);
        }

        if (table.TryGetValue("rules", out var rules) && rules is TomlTableArray ruleTables)
        {
            config.Rules = [.. ruleTables.Select(table => ParseRule(table, log)).OfType<Rule>()];
        }

        if (table.TryGetValue("hotkeys", out var hotkeys) && hotkeys is TomlTable hotkeyTable)
        {
            var parsed = new List<Hotkey>();
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
        static string[]? Strings(TomlTable table, string key) =>
            !table.TryGetValue(key, out var value) ? null : value switch
            {
                string text => [text],
                TomlArray array => [.. array.OfType<string>()],
                _ => null,
            };

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
            AppIds = Strings(table, "match_app_id"),
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

    private static Hotkey? ParseHotkey(string chord, object value, BasinLogger log)
    {
        var command = value switch
        {
            string text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            TomlArray array => [.. array.OfType<string>().Select(static part => part.Trim()).Where(static part => part.Length > 0)],
            _ => Array.Empty<string>(),
        };
        if (command.Length == 0)
        {
            log.Warn($"hotkey '{chord}' has no command, skipping");
            return null;
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

        return new Hotkey(keysym, modifiers, command);
    }
}
