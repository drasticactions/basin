using Basin.Config;
using Basin.Diagnostics;

namespace LunaWm;

internal sealed class LunaConfig
{
    public static readonly LauncherTab[] DefaultTabs =
    [
        new("Apps", [], []),
        new("Downloads", [], []),
        new("Favorites", [], []),
        new("Settings", ["Settings", "System"], []),
    ];

    public string? Path { get; private set; }

    public string? Renderer { get; private set; }

    public string Wallpaper { get; private set; } = "";

    public HandoffSettings Handoff { get; private set; } = new();

    public AccessibilitySettings Accessibility { get; private set; } = new();

    public bool XWayland { get; private set; } = true;

    public Basin.XWayland.XWaylandScale XWaylandScale { get; private set; }

    public int[] XWaylandDpiBuckets { get; private set; } = [];

    public bool XWaylandLowDensity { get; private set; } = true;

    public Basin.Config.LaunchRules Launch { get; private set; } = Basin.Config.LaunchRules.Empty;

    public double ActiveRatio { get; private set; } = 0.659;

    public double InactiveRatio { get; private set; } = 0.61;

    public double RotationFactor { get; private set; } = 30;

    public int StackGap { get; private set; } = 10;

    public double Dim { get; private set; } = 0.8;

    public int CloseTimeoutMs { get; private set; } = 10_000;

    public double TimeScale { get; private set; } = 1;

    public int EdgeBand { get; private set; } = 24;

    public int QuickLaunchHoldMs { get; private set; } = 250;

    public bool NextPrevious { get; private set; } = true;

    public Basin.Kinematics.SwipeReleaseMode SwipeRelease { get; private set; } = Basin.Kinematics.SwipeReleaseMode.Projection;

    public string BackKey { get; private set; } = "XF86Back";

    public bool Clock24 { get; private set; }

    public bool ShowSeconds { get; private set; }

    public List<LauncherTab> Tabs { get; } = [];

    public List<string> QuickLaunch { get; } = [];

    public int BannerMs { get; private set; } = 5000;

    public int DefaultExpireMs { get; private set; }

    public bool ReplaceDaemon { get; private set; }

    public bool Sound { get; private set; } = true;

    public string SearchUrl { get; private set; } = "https://duckduckgo.com/?q=%s";

    public bool JustTypeRun { get; private set; } = true;

    public bool JustTypeWeb { get; private set; } = true;

    public LockStyle LockStyle { get; private set; } = LockStyle.Password;

    public string PamService { get; private set; } = "login";

    public int IdleLockSeconds { get; private set; }

    public KeyboardConfig Keyboard { get; private set; } = new();

    public Basin.Inspector.InspectorSettings Inspector { get; private set; } = new();

    public LunaBindings Bindings { get; private set; } = null!;

    public List<LunaRule> Rules { get; } = [];

    public IReadOnlyList<Basin.Capabilities.InputRule> InputRules { get; private set; } = [];

    public static string DefaultPath() => TomlConfig.DefaultPath("luna-wm");

    public static string Template()
    {
        using var reader = new StreamReader(TemplateStream());
        return reader.ReadToEnd();
    }

    private static Stream TemplateStream() =>
        typeof(LunaConfig).Assembly.GetManifestResourceStream("luna-wm.toml")
            ?? throw new InvalidOperationException("The luna-wm.toml template is not embedded in the program.");

    public static LunaConfig Load(string? path, bool none, BasinLogger log, out string? fatal)
    {
        var config = new LunaConfig();
        var load = TomlConfig.Load(path, "luna-wm", none, TemplateStream, log);
        fatal = load.Fatal;
        config.Path = load.File;
        if (load.Table is not { } table)
        {
            config.Bindings = new LunaBindings(null, log);
            config.Tabs.AddRange(DefaultTabs);
            return config;
        }

        config.Read(new TomlReader(table, log), log);
        return config;
    }

    public static LunaConfig FromText(string text, BasinLogger log)
    {
        var table = Tomlyn.Toml.ToModel(text);
        var config = new LunaConfig();
        config.Read(new TomlReader(table, log), log);
        return config;
    }

    private void Read(TomlReader reader, BasinLogger log)
    {
        if (reader.Section("shell") is { } shell)
        {
            Renderer = shell.Text("renderer") is { Length: > 0 } renderer ? renderer : null;
            Wallpaper = shell.Text("wallpaper") ?? "";
            XWayland = shell.Flag("xwayland", XWayland);
            var scale = shell.Raw("xwayland_scale");
            if (Basin.XWayland.XWaylandScale.TryParse(scale, out var parsed))
            {
                XWaylandScale = parsed;
            }
            else
            {
                log.Warn($"[shell] xwayland_scale {scale} is not \"auto\" or a number of at least 1, auto is used");
            }

            if (shell.Numbers("xwayland_dpi_buckets") is { } buckets)
            {
                XWaylandDpiBuckets = [.. buckets.Where(static b => b > 0).Select(static b => (int)Math.Round(b))];
            }

            XWaylandLowDensity = shell.Flag("xwayland_low_density", XWaylandLowDensity);
        }

        if (reader.Section("cards") is { } cards)
        {
            ActiveRatio = Math.Clamp(cards.Number("active_ratio", ActiveRatio), 0.26, 1);
            InactiveRatio = Math.Clamp(cards.Number("inactive_ratio", InactiveRatio), 0.26, 1);
            RotationFactor = Math.Max(1, cards.Number("rotation_factor", RotationFactor));
            StackGap = Math.Max(0, cards.Number("stack_gap", StackGap));
            Dim = Math.Clamp(cards.Number("dim", Dim), 0, 1);
            CloseTimeoutMs = Math.Max(0, cards.Number("close_timeout_ms", CloseTimeoutMs));
            TimeScale = Math.Max(0, cards.Number("time_scale", TimeScale));
        }

        if (reader.Section("gestures") is { } gestures)
        {
            EdgeBand = Math.Clamp(gestures.Number("edge_band", EdgeBand), 4, 200);
            QuickLaunchHoldMs = Math.Clamp(gestures.Number("quick_launch_hold_ms", QuickLaunchHoldMs), 0, 5000);
            NextPrevious = gestures.Flag("next_previous", NextPrevious);
            SwipeRelease = gestures.Choice("swipe_release", "projection", "projection", "threshold") == "threshold"
                ? Basin.Kinematics.SwipeReleaseMode.Threshold
                : Basin.Kinematics.SwipeReleaseMode.Projection;
            BackKey = gestures.Text("back_key") is { Length: > 0 } back ? back : BackKey;
            if (Keysym.FromName(BackKey) == 0)
            {
                log.Warn($"[gestures] back_key '{BackKey}' is not a keysym name, XF86Back is used");
                BackKey = "XF86Back";
            }
        }

        if (reader.Section("status_bar") is { } bar)
        {
            Clock24 = bar.Choice("clock", "12", "12", "24") == "24";
            ShowSeconds = bar.Flag("show_seconds", ShowSeconds);
        }

        if (reader.Section("launcher") is { } launcher)
        {
            foreach (var tab in launcher.Sections("tab"))
            {
                if (tab.Text("name") is not { Length: > 0 } name)
                {
                    log.Warn($"[[launcher.tab]] a tab with no name is dropped");
                    continue;
                }

                Tabs.Add(new LauncherTab(name, tab.Words("categories") ?? [], tab.Words("apps") ?? []));
            }
        }

        if (Tabs.Count == 0)
        {
            Tabs.AddRange(DefaultTabs);
        }

        if (reader.Section("quick_launch") is { } quick)
        {
            QuickLaunch.AddRange((quick.Words("apps") ?? []).Take(6));
        }

        if (reader.Section("notifications") is { } notifications)
        {
            BannerMs = Math.Clamp(notifications.Number("banner_ms", BannerMs), 500, 60_000);
            DefaultExpireMs = Math.Max(0, notifications.Number("default_expire_ms", DefaultExpireMs));
            ReplaceDaemon = notifications.Flag("replace_daemon", ReplaceDaemon);
            Sound = notifications.Flag("sound", Sound);
        }

        if (reader.Section("just_type") is { } justType)
        {
            SearchUrl = justType.Text("search_url") is { Length: > 0 } url ? url : SearchUrl;
            JustTypeRun = justType.Flag("run", JustTypeRun);
            JustTypeWeb = justType.Flag("web", JustTypeWeb);
        }

        if (reader.Section("lock") is { } lockTable)
        {
            LockStyle = lockTable.Choice("style", "password", "pin", "password") == "pin" ? LockStyle.Pin : LockStyle.Password;
            PamService = lockTable.Text("pam_service") is { Length: > 0 } service ? service : PamService;
            IdleLockSeconds = Math.Max(0, lockTable.Number("idle_lock_seconds", IdleLockSeconds));
        }

        if (reader.Section("keyboard") is { } keyboard)
        {
            Keyboard = KeyboardConfig.Read(keyboard);
        }

        Dictionary<string, object>? bindings = null;
        if (reader.Free("keybindings") is { } keybindings)
        {
            bindings = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (chord, value) in keybindings)
            {
                bindings[chord] = value;
            }
        }

        Bindings = new LunaBindings(bindings, log);

        Launch = Basin.Config.LaunchRules.Read(reader);
        var rules = new List<LunaRule>();
        foreach (var row in reader.Sections("rule"))
        {
            var appIds = row.Words("app_id") ?? (row.Text("app_id") is { } one ? [one] : null);
            var titles = row.Words("title") ?? (row.Text("title") is { } title ? [title] : null);
            rules.Add(new LunaRule
            {
                AppIds = appIds,
                Titles = titles,
                SteamGames = WindowRule.SteamGameIds(row.Raw("steam_game"), row.Where("steam_game"), log),
                Processes = row.Words("process"),
                X11Classes = row.Words("x11_class"),
                X11Instances = row.Words("x11_instance"),
                WindowTypes = row.Words("window_type"),
                Quirks = WindowQuirks.Read(row),
                Stack = row.Choice("stack", "auto", "auto", "new", "join") switch
                {
                    "new" => StackChoice.New,
                    "join" => StackChoice.Join,
                    _ => StackChoice.Auto,
                },
                Output = row.Text("output"),
                Fullscreen = row.Raw("fullscreen") as bool?,
            });
        }

        Rules.AddRange(WindowRule.MostSpecificFirst(rules));

        if (reader.Section("input") is { } input)
        {
            InputRules = InputConfig.Read(input, reader.Log);
        }

        Inspector = Basin.Inspector.InspectorSettings.Read(reader.Section("inspector"));
        Handoff = HandoffSettings.Read(reader.Section("handoff"));
        Accessibility = AccessibilitySettings.Read(reader.Section("accessibility"));
        reader.ReportUnknown();
    }

    public LunaRule? RuleFor(string? appId, string? title) => RuleFor(new WindowFacts { AppId = appId, Title = title });

    public LunaRule? RuleFor(in WindowFacts facts)
    {
        foreach (var rule in Rules)
        {
            if (rule.Matches(facts))
            {
                return rule;
            }
        }

        return null;
    }

    public IEnumerable<string> RestartKeysChanged(LunaConfig next)
    {
        if (next.Handoff.Mode != Handoff.Mode)
        {
            yield return "handoff.mode";
        }

        if (next.Renderer != Renderer)
        {
            yield return "shell.renderer";
        }

        if (next.XWayland != XWayland)
        {
            yield return "shell.xwayland";
        }

        if (next.XWaylandLowDensity != XWaylandLowDensity)
        {
            yield return "shell.xwayland_low_density";
        }

        if (next.ReplaceDaemon != ReplaceDaemon)
        {
            yield return "notifications.replace_daemon";
        }

        if (next.PamService != PamService)
        {
            yield return "lock.pam_service";
        }
    }
}
