using System.Globalization;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

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

    public static Config Load(string? path, ILogger log)
    {
        var config = new Config();
        if (path == "false")
        {
            return config;
        }

        var file = path is { Length: > 0 } ? path : DefaultPath();
        string text;
        try
        {
            if (!File.Exists(file))
            {
                return config;
            }

            text = File.ReadAllText(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.LogWarning("cannot read {Path}: {Reason}", file, error.Message);
            return config;
        }

        TomlTable table;
        try
        {
            table = Toml.ToModel(text);
        }
        catch (TomlException error)
        {
            log.LogWarning("{Path} did not parse, keeping defaults: {Reason}", file, error.Message);
            return config;
        }

        if (table.TryGetValue("shell", out var shell) && shell is TomlTable shellTable)
        {
            config.HotCorners = Flag(shellTable, "hot_corners", config.HotCorners);
            config.Animations = Flag(shellTable, "animations", config.Animations);
            config.EdgeBand = Number(shellTable, "edge_band", (int)config.EdgeBand);
            config.MinWidth = Number(shellTable, "min_width", config.MinWidth);
            config.MaxCells = Math.Clamp(Number(shellTable, "max_cells", config.MaxCells), 1, 8);
            config.StartOutput = Math.Max(0, Number(shellTable, "start_output", config.StartOutput));
        }

        if (table.TryGetValue("rule", out var rules) && rules is TomlTableArray ruleArray)
        {
            foreach (var row in ruleArray)
            {
                if (Text(row, "app_id") is { Length: > 0 } appId)
                {
                    config.Rules.Add(new Rule(appId, Number(row, "min_width", 0)));
                }
            }
        }

        if (table.TryGetValue("ui", out var ui) && ui is TomlTable uiTable &&
            uiTable.TryGetValue("font", out var font) && font is string fontName)
        {
            config.Font = fontName;
        }

        if (table.TryGetValue("start", out var start) && start is TomlTable startTable)
        {
            if (startTable.TryGetValue("background", out var background) && background is string color)
            {
                config.Background = ParseColor(color, config.Background);
            }

            if (startTable.TryGetValue("scan_desktop_files", out var scan) && scan is bool scanning)
            {
                config.ScanDesktopFiles = scanning;
            }

            if (startTable.TryGetValue("apps_view", out var apps) && apps is bool appsView)
            {
                config.AppsView = appsView;
            }
        }

        if (table.TryGetValue("group", out var groups) && groups is TomlTableArray groupArray)
        {
            foreach (var group in groupArray)
            {
                if (group.TryGetValue("name", out var name) && name is string groupName)
                {
                    config.GroupOrder.Add(groupName);
                }
            }
        }

        if (table.TryGetValue("tile", out var tiles) && tiles is TomlTableArray tileArray)
        {
            foreach (var row in tileArray)
            {
                if (ReadTile(row, log) is { } tile)
                {
                    config.Tiles.Add(tile);
                }
            }
        }

        return config;
    }

    public static string DefaultPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome) || !Path.IsPathRooted(configHome))
        {
            configHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(configHome, "eight-wm", "eight-wm.toml");
    }

    private static Tile? ReadTile(TomlTable row, ILogger log)
    {
        var name = Text(row, "name");
        var exec = Text(row, "exec");
        var icon = Text(row, "icon");

        if (Text(row, "desktop") is { Length: > 0 } desktop)
        {
            if (DesktopEntries.Find(desktop) is not { } entry)
            {
                log.LogDebug("no desktop entry named {Desktop}; the tile is dropped", desktop);
                return null;
            }

            name ??= entry.Name;
            exec ??= entry.Exec;
            icon ??= entry.Icon;
        }

        if (name is not { Length: > 0 } || exec is not { Length: > 0 })
        {
            log.LogWarning("a [[tile]] with no name and no exec is dropped");
            return null;
        }

        return new Tile
        {
            Name = name,
            Exec = exec,
            Icon = icon,
            Size = Text(row, "size") switch
            {
                "small" => TileSize.Small,
                "wide" => TileSize.Wide,
                "large" => TileSize.Large,
                _ => TileSize.Square,
            },
            Color = ParseColor(Text(row, "color"), 0xff2d89ef),
            Group = Text(row, "group") ?? "Main",
            PeekCommand = Text(row, "peek_cmd"),
            BadgeCommand = Text(row, "badge_cmd"),
            PeekIntervalSeconds = Number(row, "peek_interval", 60),
        };
    }

    private static bool Flag(TomlTable table, string key, bool fallback) =>
        table.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;

    private static string? Text(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is string text && text.Length > 0 ? text : null;

    private static int Number(TomlTable table, string key, int fallback) =>
        table.TryGetValue(key, out var value) && value is long number ? (int)number : fallback;

    public static uint ParseColor(string? text, uint fallback)
    {
        if (text is not { Length: > 0 })
        {
            return fallback;
        }

        var digits = text.TrimStart('#');
        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return digits.Length <= 6 ? 0xff000000u | value : value;
    }
}
