using Basin.Config;

using Basin.Diagnostics;

namespace EightWm;

internal sealed class Config
{
    public string? Font { get; private set; }

    public uint Background { get; private set; } = 0xff1f4e79;

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
        }

        var rules = new List<Rule>();
        foreach (var row in reader.Sections("rule"))
        {
            if (row.Text("app_id") is { Length: > 0 } appId)
            {
                rules.Add(new Rule { AppIds = [appId], MinWidth = row.Number("min_width", 0) });
            }
        }

        config.Rules.AddRange(WindowRule.MostSpecificFirst(rules));

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

        foreach (var row in reader.Sections("tile"))
        {
            if (ReadTile(row, log) is { } tile)
            {
                config.Tiles.Add(tile);
            }
        }

        reader.ReportUnknown();
        return config;
    }

    public static string DefaultPath() => TomlConfig.DefaultPath("eight-wm");

    private static Tile? ReadTile(TomlReader row, BasinLogger log)
    {
        var name = row.Text("name");
        var exec = row.Text("exec");
        var icon = row.Text("icon");

        if (row.Text("desktop") is { Length: > 0 } desktop)
        {
            if (DesktopEntries.Find(desktop) is not { } entry)
            {
                log.Debug($"no desktop entry named {desktop}; the tile is dropped");
                return null;
            }

            name ??= entry.Name;
            exec ??= entry.Exec;
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
