using Basin.Config;
using Basin.WindowManager;
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

        var reader = new TomlReader(table, log);

        if (reader.Text("main_modifier") is { } modifierName)
        {
            config.MainModifier = modifierName.ToLowerInvariant() switch
            {
                "alt" => Modifiers.Alt,
                _ => Modifiers.Super,
            };
        }

        config.TerminalCommand = reader.Words("terminal_cmd") ?? config.TerminalCommand;
        config.LauncherCommand = reader.Words("launcher_cmd") ?? config.LauncherCommand;
        config.LockCommand = reader.Words("lock_cmd") ?? config.LockCommand;
        config.DesktopWallpaper = reader.Flag("desktop_wallpaper", config.DesktopWallpaper);

        if (reader.Free("ui") is { } uiTable)
        {
            Theme.Apply(uiTable);
        }

        if (reader.FreeArray("rules") is { } ruleTables)
        {
            config.Rules = WindowRule.MostSpecificFirst(
                ruleTables.Select(rule => ParseRule(rule, log)).OfType<Rule>());
        }

        if (reader.Free("hotkeys") is { } hotkeyTable)
        {
            var parsed = new List<Hotkey>();
            foreach (var (chord, value) in hotkeyTable)
            {
                if (HotkeyParser.Parse(chord, value, log) is { Unbinds: false } hotkey)
                {
                    parsed.Add(hotkey);
                }
            }

            config.Hotkeys = parsed;
        }

        reader.ReportUnknown();
        return config;
    }

    private static Rule? ParseRule(TomlTable table, BasinLogger log)
    {
        bool? requireCsdOnly = null;
        bool? requireNoParent = null;
        foreach (var prop in WindowRule.Strings(table, "match_props") ?? [])
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
            AppIds = WindowRule.Strings(table, "match_app_id"),
            AppIdPrefixes = WindowRule.Strings(table, "match_app_id_prefix"),
            Titles = WindowRule.Strings(table, "title"),
            AppIdRegex = WindowRule.Pattern(table, "app_id_regex", log),
            TitleRegex = WindowRule.Pattern(table, "title_regex", log),
            RequireCsdOnly = requireCsdOnly,
            RequireNoParent = requireNoParent,
            ForceSsd = table.TryGetValue("force_ssd", out var force) && force is true,
            SwallowTop = table.TryGetValue("swallow_top", out var swallow) && swallow is long pixels
                ? (int)pixels
                : null,
        };
    }
}
