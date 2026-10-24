using Basin.Config;

using Basin.Diagnostics;

namespace DeskbarWm;

internal sealed class Config
{
    public FocusMode FocusMode { get; private set; } = FocusMode.Click;

    public int DeskbarWidth { get; private set; }

    public int IconSize { get; set; } = 32;

    public bool ShowLabels { get; set; } = true;

    public bool SortTeams { get; set; }

    public bool ExpandWindows { get; set; }

    public bool ExpandNewTeams { get; set; }

    public bool AlwaysOnTop { get; set; }

    public bool AutoRaise { get; set; }

    public bool AutoHide { get; set; }

    public string[] TrayApplets { get; private set; } = ["workspaces", "clock"];

    public bool ClockShowSeconds { get; set; }

    public bool ClockShowDayOfWeek { get; set; }

    public bool ClockShowTimeZone { get; set; }

    public int RecentDocumentsCount { get; private set; } = 10;

    public int RecentFoldersCount { get; private set; } = 10;

    public int RecentApplicationsCount { get; private set; } = 10;

    public bool MenuCategories { get; private set; } = true;

    public string HaikuIconDirectory { get; private set; } = string.Empty;

    public string DesktopWallpaper { get; private set; } = string.Empty;

    public uint DesktopColor { get; private set; } = 0x336698FF;

    public string DesktopScaleMode { get; private set; } = "fill";

    public int WorkspaceRows { get; private set; } = 2;

    public int WorkspaceColumns { get; private set; } = 2;

    public DeskbarPlacement Placement { get; private set; } = DeskbarPlacement.Default;

    public bool RaiseOnFocus { get; private set; } = true;

    public string[] TerminalCommand { get; private set; } = ["foot"];

    public int SnapDistance { get; private set; } = 10;

    public bool StackAndTile { get; private set; } = true;

    public IReadOnlyList<Hotkey> Hotkeys { get; private set; } = [];

    public IReadOnlyList<Rule> Rules { get; private set; } = [];

    public void ApplyPlacement(DeskbarPlacement placement) => Placement = placement;

    public void SavePlacement(DeskbarPlacement placement, BasinLogger log)
    {
        Placement = placement;
        Save(log, "the Deskbar placement", document =>
        {
            document.Set("deskbar", "orientation", TomlValue.From(placement.Orientation == BarOrientation.Horizontal ? "horizontal" : "vertical"));
            document.Set("deskbar", "side", TomlValue.From(placement.Side == BarSide.Left ? "left" : "right"));
            document.Set("deskbar", "end", TomlValue.From(placement.End == BarEnd.Top ? "top" : "bottom"));
            document.Set("deskbar", "state", TomlValue.From(placement.State switch
            {
                DeskbarState.Mini => "mini",
                DeskbarState.Full => "full",
                _ => "expando",
            }));
        });
    }

    public void SaveKey(string section, string key, TomlValue value, BasinLogger log) =>
        Save(log, $"{section}.{key}", document => document.Set(section, key, value));

    private static void Save(BasinLogger log, string what, Action<TomlDocument> edit)
    {
        var path = TomlConfig.DefaultPath("deskbar-wm");
        try
        {
            var document = File.Exists(path) ? TomlDocument.Load(path) : TomlDocument.Parse(string.Empty);
            edit(document);
            document.Save(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            log.Warn(error, $"config: cannot save {what} to {path}");
        }
    }

    public static Config Load(bool skipFile, BasinLogger log)
    {
        var config = new Config();
        Theme.Reset();
        if (skipFile)
        {
            return config;
        }

        var load = TomlConfig.Load(null, "deskbar-wm", none: false, TemplateStream, log);
        if (load.Table is not { } table)
        {
            return config;
        }

        var reader = new TomlReader(table, log);

        if (reader.Section("deskbar") is { } deskbar)
        {
            var orientation = deskbar.Choice("orientation", "vertical", "vertical", "horizontal") == "horizontal"
                ? BarOrientation.Horizontal
                : BarOrientation.Vertical;
            var side = deskbar.Choice("side", "right", "left", "right") == "left" ? BarSide.Left : BarSide.Right;
            var end = deskbar.Choice("end", "top", "top", "bottom") == "bottom" ? BarEnd.Bottom : BarEnd.Top;
            var state = deskbar.Choice("state", "expando", "mini", "expando", "full") switch
            {
                "mini" => DeskbarState.Mini,
                "full" => DeskbarState.Full,
                _ => DeskbarState.Expando,
            };
            config.Placement = new DeskbarPlacement(orientation, side, end, state).Normalize(out var warning);
            if (warning is not null)
            {
                log.Warn($"{warning}");
            }

            config.DeskbarWidth = deskbar.Number("width", config.DeskbarWidth);
            config.IconSize = Math.Clamp(deskbar.Number("icon-size", config.IconSize), 16, 96);
            config.ShowLabels = deskbar.Flag("show-labels", config.ShowLabels);
            config.SortTeams = deskbar.Flag("sort-teams", config.SortTeams);
            config.ExpandWindows = deskbar.Flag("expand-windows", config.ExpandWindows);
            config.ExpandNewTeams = deskbar.Flag("expand-new-teams", config.ExpandNewTeams);
            config.AlwaysOnTop = deskbar.Flag("always-on-top", config.AlwaysOnTop);
            config.AutoRaise = deskbar.Flag("auto-raise", config.AutoRaise);
            config.AutoHide = deskbar.Flag("auto-hide", config.AutoHide);

            if (deskbar.Section("tray") is { } tray)
            {
                config.TrayApplets = tray.Words("applets") ?? config.TrayApplets;
            }

            if (deskbar.Section("menu") is { } menu)
            {
                config.RecentDocumentsCount = menu.Number("recent-documents", config.RecentDocumentsCount);
                config.RecentFoldersCount = menu.Number("recent-folders", config.RecentFoldersCount);
                config.RecentApplicationsCount = menu.Number("recent-applications", config.RecentApplicationsCount);
                config.MenuCategories = menu.Flag("categories", config.MenuCategories);
            }

            if (deskbar.Section("clock") is { } clock)
            {
                config.ClockShowSeconds = clock.Flag("show-seconds", config.ClockShowSeconds);
                config.ClockShowDayOfWeek = clock.Flag("show-day-of-week", config.ClockShowDayOfWeek);
                config.ClockShowTimeZone = clock.Flag("show-time-zone", config.ClockShowTimeZone);
            }
        }

        if (reader.Section("icons") is { } icons)
        {
            config.HaikuIconDirectory = icons.Text("haiku-directory") ?? config.HaikuIconDirectory;
        }

        if (reader.Section("desktop") is { } desktop)
        {
            config.DesktopWallpaper = desktop.Text("wallpaper") ?? config.DesktopWallpaper;
            if (TomlColor.Rgba(desktop.Text("color")) is { } desktopColor)
            {
                config.DesktopColor = desktopColor;
            }

            config.DesktopScaleMode = desktop.Choice("scale", "fill", "fill", "fit", "center", "tile");
        }

        if (reader.Section("workspaces") is { } workspaces)
        {
            config.WorkspaceRows = workspaces.Number("rows", config.WorkspaceRows);
            config.WorkspaceColumns = workspaces.Number("columns", config.WorkspaceColumns);
        }

        if (reader.Section("focus") is { } focus)
        {
            config.FocusMode = focus.Choice("mode", "click", "click", "follow-mouse", "follow-mouse-warping") switch
            {
                "follow-mouse" => FocusMode.FollowMouse,
                "follow-mouse-warping" => FocusMode.FollowMouseWarping,
                _ => FocusMode.Click,
            };
            config.RaiseOnFocus = focus.Flag("raise-on-focus", config.RaiseOnFocus);
        }

        if (reader.Section("stack-and-tile") is { } sat)
        {
            config.StackAndTile = sat.Flag("enabled", config.StackAndTile);
            config.SnapDistance = sat.Number("snap-distance", config.SnapDistance);
        }

        if (reader.Section("look") is { } look)
        {
            Theme.Apply(look);
        }

        config.TerminalCommand = reader.Words("terminal") ?? config.TerminalCommand;

        if (reader.Free("bindings") is { } bindings)
        {
            var parsed = new List<Hotkey>();
            foreach (var (chord, value) in bindings)
            {
                if (HotkeyParser.Parse(chord, value, log, IsAction) is { Unbinds: false } hotkey)
                {
                    parsed.Add(hotkey);
                }
            }

            config.Hotkeys = parsed;
        }

        if (reader.FreeArray("rule") is { } ruleTables)
        {
            config.Rules = WindowRule.MostSpecificFirst(
                ruleTables.Select(rule => ParseRule(rule, log)).OfType<Rule>());
        }

        reader.ReportUnknown();
        return config;
    }

    private static bool IsAction(string text) =>
        text is "close" or "zoom" or "minimize" or "terminal" or "previous-workspace"
        || (text.StartsWith("workspace ", StringComparison.Ordinal) && int.TryParse(text[10..], out _));

    private static Rule? ParseRule(Tomlyn.Model.TomlTable table, BasinLogger log)
    {
        int? workspace = null;
        if (table.TryGetValue("workspace", out var value) && value is long index)
        {
            workspace = (int)index;
        }

        return new Rule
        {
            AppIds = WindowRule.Strings(table, "app-id"),
            AppIdPrefixes = WindowRule.Strings(table, "app-id-prefix"),
            Titles = WindowRule.Strings(table, "title"),
            AppIdRegex = WindowRule.Pattern(table, "app-id-regex", log),
            TitleRegex = WindowRule.Pattern(table, "title-regex", log),
            Workspace = workspace,
            AllWorkspaces = table.TryGetValue("all-workspaces", out var all) && all is true,
        };
    }

    private static Stream TemplateStream() =>
        typeof(Config).Assembly.GetManifestResourceStream("deskbar-wm.toml")
            ?? throw new InvalidOperationException("The deskbar-wm.toml template is not embedded in the program.");
}
