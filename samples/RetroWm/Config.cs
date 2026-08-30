using Basin.Config;
using Basin.WindowManager;
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

        var reader = new TomlReader(table, log);

        if (reader.Text("main_modifier") is { } modifierName)
        {
            config.MainModifier = modifierName.ToLowerInvariant() switch
            {
                "super" or "logo" or "win" or "mod4" => Modifiers.Super,
                "ctrl" or "control" => Modifiers.Ctrl,
                _ => Modifiers.Alt,
            };
        }

        config.TerminalCommand = reader.Words("terminal_cmd") ?? config.TerminalCommand;

        if (reader.Text("decorations") is { } mode)
        {
            config.Decorations = mode.ToLowerInvariant() switch
            {
                "prefer-ssd" or "prefer" => DecorationPreference.PreferSsd,
                "csd" or "none" => DecorationPreference.Csd,
                _ => DecorationPreference.ForceSsd,
            };
        }

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
            var parsed = new List<HotkeyBinding>();
            foreach (var (chord, value) in hotkeyTable)
            {
                if (HotkeyParser.Parse(chord, value, log, static name => ActionFromName(name) is not null)
                    is { Unbinds: false } hotkey)
                {
                    parsed.Add(new HotkeyBinding(
                        hotkey.Keysym,
                        hotkey.ModifierMask,
                        hotkey.Action is { } name ? ActionFromName(name) : null,
                        hotkey.Command));
                }
            }

            config.Hotkeys = parsed;
        }

        reader.ReportUnknown();
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
            AppIds = WindowRule.Strings(table, "match_app_id", "app_id"),
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
