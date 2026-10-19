using Basin.Config;
using Basin.Freedesktop;

using Basin.Diagnostics;

namespace EightWm;

internal sealed class Config
{
    public string? Font { get; private set; }

    public uint Background { get; private set; } = 0xff1f4e79;

    public HandoffSettings Handoff { get; private set; } = new();

    public bool ScanDesktopFiles { get; private set; } = true;

    public bool AppsView { get; private set; } = true;

    public List<string> GroupOrder { get; } = [];

    public List<Tile> Tiles { get; } = [];

    public List<Rule> Rules { get; } = [];

    public bool HotCorners { get; private set; } = true;

    public bool Animations { get; private set; } = true;

    public double EdgeBand { get; private set; } = 20;

    public int MinWidth { get; private set; } = 500;

    public int MaxCells { get; private set; } = 4;

    public int StartOutput { get; private set; }

    public bool Dark { get; private set; } = true;

    public uint Accent { get; private set; } = Shell.DefaultAccent;

    public Basin.XWayland.XWaylandScale XWaylandScale { get; private set; }

    public int[] XWaylandDpiBuckets { get; private set; } = [];

    public bool XWaylandLowDensity { get; private set; } = true;

    public Basin.Config.LaunchRules Launch { get; private set; } = Basin.Config.LaunchRules.Empty;

    public int IdleLockSeconds { get; private set; }

    public string PamService { get; private set; } = "login";

    public string LockPicture { get; private set; } = "";

    public bool KeyboardEnabled { get; private set; } = true;

    public IReadOnlyList<string> KeyboardLayouts { get; private set; } = [];

    public Basin.SoftKeyboard.KeyboardSize KeyboardSize { get; private set; } = Basin.SoftKeyboard.KeyboardSize.Medium;

    public string KeyboardChord { get; private set; } = "Super+k";

    public Basin.Inspector.InspectorSettings Inspector { get; private set; } = new();

    public string InspectorChord { get; private set; } = "Super+F12";

    public static Config Load(string? path, BasinLogger log)
    {
        var config = new Config();
        if (path == "false")
        {
            return config;
        }

        var file = path is { Length: > 0 } ? path : DefaultPath();
        if (TomlConfig.Read(file, log) is not { } table)
        {
            return config;
        }

        var reader = new TomlReader(table, log);

        if (reader.Section("shell") is { } shell)
        {
            config.HotCorners = shell.Flag("hot_corners", config.HotCorners);
            config.Animations = shell.Flag("animations", config.Animations);
            config.EdgeBand = shell.Number("edge_band", (int)config.EdgeBand);
            config.MinWidth = shell.Number("min_width", config.MinWidth);
            config.MaxCells = Math.Clamp(shell.Number("max_cells", config.MaxCells), 1, 8);
            config.StartOutput = Math.Max(0, shell.Number("start_output", config.StartOutput));
            config.Dark = shell.Text("theme") switch
            {
                "light" => false,
                "dark" or null => true,
                var other => Warn(log, $"[shell] theme '{other}' is neither dark nor light; dark is used", true),
            };
            config.Accent = TomlColor.Argb(shell.Text("accent"), config.Accent) | 0xff000000;
            var scale = shell.Raw("xwayland_scale");
            config.XWaylandScale = Basin.XWayland.XWaylandScale.TryParse(scale, out var parsed)
                ? parsed
                : Warn(log, $"[shell] xwayland_scale {scale} is not \"auto\" or a number of at least 1; auto is used", Basin.XWayland.XWaylandScale.Auto);
            if (shell.Numbers("xwayland_dpi_buckets") is { } buckets)
            {
                config.XWaylandDpiBuckets = [.. buckets.Where(static b => b > 0).Select(static b => (int)Math.Round(b))];
            }

            config.XWaylandLowDensity = shell.Flag("xwayland_low_density", config.XWaylandLowDensity);
        }

        config.Launch = Basin.Config.LaunchRules.Read(reader);
        var rules = new List<Rule>();
        foreach (var row in reader.Sections("rule"))
        {
            var rule = new Rule
            {
                AppIds = row.Text("app_id") is { Length: > 0 } appId ? [appId] : null,
                SteamGames = WindowRule.SteamGameIds(row.Raw("steam_game"), row.Where("steam_game"), log),
                Processes = row.Words("process"),
                X11Classes = row.Words("x11_class"),
                X11Instances = row.Words("x11_instance"),
                WindowTypes = row.Words("window_type"),
                Quirks = WindowQuirks.Read(row),
                MinWidth = row.Number("min_width", 0),
            };
            if (rule.HasAppCriteria || rule.FactCriteriaCount > 0 || rule.HasTitleCriteria)
            {
                rules.Add(rule);
            }
        }

        config.Rules.AddRange(WindowRule.MostSpecificFirst(rules));

        if (reader.Section("lock") is { } lockTable)
        {
            config.IdleLockSeconds = Math.Max(0, lockTable.Number("idle_seconds", config.IdleLockSeconds));
            config.PamService = lockTable.Text("pam_service") is { Length: > 0 } service ? service : config.PamService;
            config.LockPicture = lockTable.Text("picture") ?? config.LockPicture;
        }

        if (reader.Section("keyboard") is { } keyboard)
        {
            config.KeyboardEnabled = keyboard.Flag("enabled", config.KeyboardEnabled);
            config.KeyboardLayouts = keyboard.Words("layouts") ?? config.KeyboardLayouts;
            config.KeyboardSize = Basin.SoftKeyboard.KeyboardSizes.Parse(keyboard.Choice("size", "m", "xs", "s", "m", "l"), config.KeyboardSize);
        }

        if (reader.Section("keybindings") is { } bindings)
        {
            config.KeyboardChord = bindings.Text("keyboard") ?? config.KeyboardChord;
            config.InspectorChord = bindings.Text("inspector") ?? config.InspectorChord;
        }

        if (reader.Section("ui") is { } ui && ui.Text("font") is { } fontName)
        {
            config.Font = fontName;
        }

        if (reader.Section("start") is { } start)
        {
            config.Background = TomlColor.Argb(start.Text("background"), config.Background);
            config.ScanDesktopFiles = start.Flag("scan_desktop_files", config.ScanDesktopFiles);
            config.AppsView = start.Flag("apps_view", config.AppsView);
        }

        foreach (var group in reader.Sections("group"))
        {
            if (group.Text("name") is { } groupName)
            {
                config.GroupOrder.Add(groupName);
            }
        }

        var desktop = new DesktopEntries();
        foreach (var row in reader.Sections("tile"))
        {
            if (ReadTile(row, desktop, log) is { } tile)
            {
                config.Tiles.Add(tile);
            }
        }

        config.Inspector = Basin.Inspector.InspectorSettings.Read(reader.Section("inspector"));
        config.Handoff = HandoffSettings.Read(reader.Section("handoff"));
        reader.ReportUnknown();
        return config;
    }

    private static T Warn<T>(BasinLogger log, string message, T value)
    {
        log.Warn($"{message}");
        return value;
    }

    public static string DefaultPath() => TomlConfig.DefaultPath("eight-wm");

    private static Tile? ReadTile(TomlReader row, DesktopEntries desktop, BasinLogger log)
    {
        var name = row.Text("name");
        var exec = row.Text("exec");
        var icon = row.Text("icon");

        if (row.Text("desktop") is { Length: > 0 } desktopId)
        {
            if (desktop.Find(desktopId) is not { } entry)
            {
                log.Debug($"no desktop entry named {desktopId}; the tile is dropped");
                return null;
            }

            name ??= entry.Name;
            exec ??= DesktopLaunch.CommandFor(entry);
            icon ??= entry.Icon;
        }

        if (name is not { Length: > 0 } || exec is not { Length: > 0 })
        {
            log.Warn($"a [[tile]] with no name and no exec is dropped");
            return null;
        }

        return new Tile
        {
            Name = name,
            Exec = exec,
            Icon = icon,
            DesktopId = row.Text("desktop") is { Length: > 0 } tileDesktop ? Basin.Config.LaunchRule.NormalizeDesktopId(tileDesktop) : null,
            Size = row.Text("size") switch
            {
                "small" => TileSize.Small,
                "wide" => TileSize.Wide,
                "large" => TileSize.Large,
                _ => TileSize.Square,
            },
            Color = TomlColor.Argb(row.Text("color"), 0xff2d89ef),
            Group = row.Text("group") ?? "Main",
            PeekCommand = row.Text("peek_cmd"),
            BadgeCommand = row.Text("badge_cmd"),
            PeekIntervalSeconds = row.Number("peek_interval", 60),
        };
    }
}
