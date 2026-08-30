using Basin;
using Basin.Capabilities;
using Basin.Config;
using Basin.Effects;
using Tomlyn.Model;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed class Config
{
    private static readonly (string Chord, string Action)[] DefaultBindings =
    [
        ("Alt+Escape", "quit"),
        ("Alt+Tab", "cycle"),
        ("Alt+s", "cycle-scale"),
        ("Alt+Left", "workspace-prev"),
        ("Alt+Right", "workspace-next"),
        ("Alt+Shift+Left", "carry-prev"),
        ("Alt+Shift+Right", "carry-next"),
        ("Alt+n", "workspace-new"),
    ];

    public string Renderer { get; set; } = "vulkan";

    public int Outputs { get; set; } = 1;

    public double[] Scales { get; set; } = [];

    public long Frames { get; set; }

    public bool Transactions { get; set; } = true;

    public bool Offload { get; set; } = true;

    public bool FullRepaint { get; set; }

    public bool DamageTint { get; set; }

    public FrameStyle FrameStyle { get; set; } = FrameStyle.Beos;

    public int CornerRadius { get; set; }

    public OutputColorProfileSource ColorSource { get; set; } = OutputColorProfileSource.Edid;

    public string? IccProfile { get; set; }

    public bool Hdr { get; set; }

    public double? NightLight { get; set; }

    public bool Wobbly { get; set; }

    public string? OpenAnimation { get; set; }

    public string? CloseAnimation { get; set; }

    public bool Switcher { get; set; }

    public string? MinimizeAnimation { get; set; }

    public bool Highlight { get; set; }

    public bool DimInactive { get; set; }

    public bool DropShadow { get; set; }

    public bool SlideBack { get; set; }

    public bool Stretch { get; set; }

    public bool Notifications { get; set; }

    public bool ShakeCursor { get; set; }

    public bool MouseClick { get; set; }

    public bool MouseMark { get; set; }

    public bool TrackMouse { get; set; }

    public bool TouchPoints { get; set; }

    public bool SystemBell { get; set; }

    public StartupFeedbackKind StartupFeedback { get; set; } = StartupFeedbackKind.None;

    public bool BlendChanges { get; set; }

    public bool ScreenTransform { get; set; }

    public ColorBlindnessMode ColorBlindness { get; set; } = ColorBlindnessMode.Protanopia;

    public double ColorBlindnessIntensity { get; set; } = 1.0;

    public ZoomTracking ZoomTracking { get; set; } = ZoomTracking.Proportional;

    public IReadOnlyList<string> Post { get; set; } = [];

    public HashSet<string> FromFlags { get; } = new(StringComparer.Ordinal);

    public HashSet<string> FromFile { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<Binding> Bindings { get; private set; } = [];

    public IReadOnlyList<Rule> Rules { get; private set; } = [];

    public IReadOnlyDictionary<string, OutputSetting> OutputSettings { get; private set; } =
        new Dictionary<string, OutputSetting>(StringComparer.Ordinal);

    public OutputSetting? OutputSettingFor(string name) =>
        OutputSettings.TryGetValue(name, out var setting) ? setting : null;

    public static KeyAction? ActionFromName(string name) => name switch
    {
        "quit" => KeyAction.Quit,
        "cycle" => KeyAction.Cycle,
        "switcher" => KeyAction.Switcher,
        "cycle-focus" => KeyAction.CycleFocus,
        "cycle-scale" => KeyAction.CycleScale,
        "workspace-next" => KeyAction.WorkspaceNext,
        "workspace-prev" => KeyAction.WorkspacePrev,
        "carry-next" => KeyAction.CarryNext,
        "carry-prev" => KeyAction.CarryPrev,
        "workspace-new" => KeyAction.WorkspaceNew,
        "zoom-in" => KeyAction.ZoomIn,
        "zoom-out" => KeyAction.ZoomOut,
        "zoom-reset" => KeyAction.ZoomReset,
        "mark-undo" => KeyAction.MarkUndo,
        "mark-clear" => KeyAction.MarkClear,
        "bell" => KeyAction.Bell,
        _ => null,
    };

    private static readonly string[] SharedKeys =
        ["renderer", "outputs", "scale", "frames", "offload", "full_repaint", "damage_tint"];

    public static string DefaultPath() => TomlConfig.DefaultPath("tinycomp");

    public static Config Load(string? path, BasinLogger log, out string? fatal)
    {
        var config = new Config();
        config.SeedBindings(log);
        fatal = null;

        if (path == "false")
        {
            return config;
        }

        var named = path is { Length: > 0 };
        var file = named ? path! : DefaultPath();
        if (!named && !File.Exists(file))
        {
            Seed(file, log);
        }

        var table = TomlConfig.Read(file, out var failure);
        if (table is null)
        {
            if (named)
            {
                fatal = $"{file}: {failure}";
            }
            else if (File.Exists(file))
            {
                log.Warn($"{file} did not parse, keeping defaults: {failure}");
            }

            return config;
        }

        config.Apply(new TomlReader(table, log));
        return config;
    }

    public static string Template()
    {
        using var stream = typeof(Config).Assembly.GetManifestResourceStream("tinycomp.toml")
            ?? throw new InvalidOperationException("tinycomp.toml is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Seed(string file, BasinLogger log)
    {
        try
        {
            if (Path.GetDirectoryName(file) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
            {
                using var writer = new StreamWriter(stream);
                writer.Write(Template());
            }

            log.Info($"wrote the default configuration to {file}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"cannot write {file}, keeping the built-in defaults: {error.Message}");
        }
    }

    public Rule? RuleFor(string? appId, string? title)
    {
        foreach (var rule in Rules)
        {
            if (rule.MatchesText(appId, title))
            {
                return rule;
            }
        }

        return null;
    }

    private void SeedBindings(BasinLogger log)
    {
        var seeded = new List<Binding>(DefaultBindings.Length);
        foreach (var (chord, action) in DefaultBindings)
        {
            if (HotkeyParser.Parse(chord, action, log, static name => ActionFromName(name) is not null) is { } hotkey)
            {
                seeded.Add(new Binding(hotkey.Keysym, hotkey.ModifierMask, ActionFromName(action), null));
            }
        }

        Bindings = seeded;
    }

    private void Apply(TomlReader reader)
    {
        var log = reader.Log;

        if (reader.Section("compositor") is { } compositor)
        {
            foreach (var key in SharedKeys)
            {
                if (compositor.Table.ContainsKey(key))
                {
                    FromFile.Add(key);
                }
            }

            Renderer = compositor.Text("renderer") ?? Renderer;
            Outputs = compositor.Number("outputs", Outputs);
            Scales = compositor.Numbers("scale") ?? Scales;
            Frames = compositor.Number("frames", (int)Frames);
            Transactions = compositor.Flag("transactions", Transactions);
            Offload = compositor.Flag("offload", Offload);
            FullRepaint = compositor.Flag("full_repaint", FullRepaint);
            DamageTint = compositor.Flag("damage_tint", DamageTint);
        }

        if (reader.Section("frame") is { } frame)
        {
            FrameStyle = frame.Choice("style", "beos", "beos", "flat", "none") switch
            {
                "flat" => FrameStyle.Flat,
                "none" => FrameStyle.None,
                _ => FrameStyle.Beos,
            };
            CornerRadius = frame.Number("corner_radius", CornerRadius);
        }

        if (reader.Section("color") is { } color)
        {
            ColorSource = color.Choice("source", "edid", "edid", "srgb", "icc") switch
            {
                "srgb" => OutputColorProfileSource.Srgb,
                "icc" => OutputColorProfileSource.Icc,
                _ => OutputColorProfileSource.Edid,
            };
            IccProfile = color.Text("icc");
            Hdr = color.Flag("hdr", Hdr);
            var kelvin = color.Number("night_light", 0.0);
            NightLight = kelvin > 0 ? kelvin : null;
            if (ColorSource == OutputColorProfileSource.Icc && IccProfile is null)
            {
                log.Warn($"[color] source = \"icc\" names no profile in [color] icc, describing the outputs from their EDID");
                ColorSource = OutputColorProfileSource.Edid;
            }
        }

        if (reader.Section("effects") is { } effects)
        {
            Wobbly = effects.Flag("wobbly", Wobbly);
            OpenAnimation = Animation(effects.Choice(
                "open", "none", "none", "fade", "zoom", "glide", "sheet"));
            CloseAnimation = Animation(effects.Choice(
                "close", "none", "none", "fade", "zoom", "fire", "fire-gpu", "glide", "sheet", "fall-apart"));
            MinimizeAnimation = Animation(effects.Choice(
                "minimize", "none", "none", "magic-lamp", "squash"));
            Switcher = effects.Flag("switcher", Switcher);
            Highlight = effects.Flag("highlight", Highlight);
            DimInactive = effects.Flag("dim_inactive", DimInactive);
            DropShadow = effects.Flag("drop_shadow", DropShadow);
            SlideBack = effects.Flag("slide_back", SlideBack);
            Stretch = effects.Flag("stretch", Stretch);
            Notifications = effects.Flag("notifications", Notifications);
            ShakeCursor = effects.Flag("shake_cursor", ShakeCursor);
            MouseClick = effects.Flag("mouse_click", MouseClick);
            MouseMark = effects.Flag("mouse_mark", MouseMark);
            TrackMouse = effects.Flag("track_mouse", TrackMouse);
            TouchPoints = effects.Flag("touch_points", TouchPoints);
            SystemBell = effects.Flag("system_bell", SystemBell);
            BlendChanges = effects.Flag("blend_changes", BlendChanges);
            ScreenTransform = effects.Flag("screen_transform", ScreenTransform);
            StartupFeedback = effects.Choice(
                "startup_feedback", "none", "none", "bouncing", "blinking", "passive") switch
            {
                "bouncing" => StartupFeedbackKind.Bouncing,
                "blinking" => StartupFeedbackKind.Blinking,
                "passive" => StartupFeedbackKind.Passive,
                _ => StartupFeedbackKind.None,
            };
            ColorBlindness = effects.Choice(
                "color_blindness", "protanopia", "protanopia", "deuteranopia", "tritanopia", "monochrome") switch
            {
                "deuteranopia" => ColorBlindnessMode.Deuteranopia,
                "tritanopia" => ColorBlindnessMode.Tritanopia,
                "monochrome" => ColorBlindnessMode.Monochrome,
                _ => ColorBlindnessMode.Protanopia,
            };
            ColorBlindnessIntensity = Math.Clamp(
                effects.Number("color_blindness_intensity", ColorBlindnessIntensity), 0, 1);
            ZoomTracking = effects.Choice(
                "zoom_tracking", "proportional",
                "proportional", "centered", "push", "disabled", "centered-strict") switch
            {
                "centered" => ZoomTracking.Centered,
                "push" => ZoomTracking.Push,
                "disabled" => ZoomTracking.Disabled,
                "centered-strict" => ZoomTracking.CenteredStrict,
                _ => ZoomTracking.Proportional,
            };
            Post = PostStages(effects, log);
        }

        if (reader.Free("output") is { } outputs)
        {
            var parsed = new Dictionary<string, OutputSetting>(StringComparer.Ordinal);
            foreach (var (name, value) in outputs)
            {
                if (value is TomlTable table)
                {
                    parsed[name] = ParseOutputSetting(name, table, log);
                }
                else
                {
                    log.Warn($"[output.\"{name}\"] is not a table, ignored");
                }
            }

            OutputSettings = parsed;
        }

        if (reader.Free("bindings") is { } bindings)
        {
            Bindings = MergeBindings(bindings, log);
        }

        if (reader.FreeArray("rule") is { } rules)
        {
            Rules = WindowRule.MostSpecificFirst(rules.Select(row => ParseRule(row, log)).OfType<Rule>());
        }

        reader.ReportUnknown();
    }

    private IReadOnlyList<Binding> MergeBindings(TomlTable table, BasinLogger log)
    {
        var merged = new List<Binding>(Bindings);
        foreach (var (chord, value) in table)
        {
            if (HotkeyParser.Parse(chord, value, log, static name => ActionFromName(name) is not null)
                is not { } hotkey)
            {
                continue;
            }

            merged.RemoveAll(existing =>
                existing.Keysym == hotkey.Keysym && existing.ModifierMask == hotkey.ModifierMask);
            if (hotkey.Unbinds)
            {
                continue;
            }

            merged.Add(new Binding(
                hotkey.Keysym,
                hotkey.ModifierMask,
                hotkey.Action is { } name ? ActionFromName(name) : null,
                hotkey.Command));
        }

        return merged;
    }

    private static Rule? ParseRule(TomlTable table, BasinLogger log)
    {
        var appIds = WindowRule.Strings(table, "app_id");
        var titleRegex = WindowRule.Pattern(table, "title_regex", log);
        if (appIds is null && titleRegex is null)
        {
            log.Warn($"a [[rule]] naming neither app_id nor title_regex is dropped");
            return null;
        }

        int? Number(string key) =>
            table.TryGetValue(key, out var value) && value is long number ? (int)number : null;

        string? Text(string key) =>
            table.TryGetValue(key, out var value) && value is string { Length: > 0 } text ? text : null;

        bool? Flag(string key) =>
            table.TryGetValue(key, out var value) && value is bool flag ? flag : null;

        return new Rule
        {
            AppIds = appIds,
            TitleRegex = titleRegex,
            FrameStyle = Text("frame") switch
            {
                "beos" => global::TinyComp.FrameStyle.Beos,
                "flat" => global::TinyComp.FrameStyle.Flat,
                "none" => global::TinyComp.FrameStyle.None,
                _ => null,
            },
            CornerRadius = Number("corner_radius"),
            Effects = Flag("effects"),
            Wobbly = Flag("wobbly"),
            Open = Animation(Text("open")),
            Close = Animation(Text("close")),
            Workspace = Number("workspace"),
            X = Number("x"),
            Y = Number("y"),
            Width = Number("width"),
            Height = Number("height"),
        };
    }

    private static OutputSetting ParseOutputSetting(string name, TomlTable table, BasinLogger log)
    {
        double? scale = null;
        OutputTransform? transform = null;
        (int Width, int Height, int? Refresh)? mode = null;
        foreach (var (key, value) in table)
        {
            switch (key)
            {
                case "scale" when value is double fractional:
                    scale = fractional;
                    break;
                case "scale" when value is long integer:
                    scale = integer;
                    break;
                case "transform" when value is string text:
                    transform = ParseTransform(text, name, log);
                    break;
                case "mode" when value is string text:
                    mode = ParseMode(text, name, log);
                    break;
                default:
                    log.Warn($"[output.\"{name}\"] {key}: unknown key or wrong type, ignored");
                    break;
            }
        }

        return new OutputSetting { Scale = scale, Transform = transform, Mode = mode };
    }

    private static OutputTransform? ParseTransform(string text, string name, BasinLogger log)
    {
        switch (text)
        {
            case "normal": return OutputTransform.Normal;
            case "90": return OutputTransform.Rotate90;
            case "180": return OutputTransform.Rotate180;
            case "270": return OutputTransform.Rotate270;
            case "flipped": return OutputTransform.Flipped;
            case "flipped-90": return OutputTransform.Flipped90;
            case "flipped-180": return OutputTransform.Flipped180;
            case "flipped-270": return OutputTransform.Flipped270;
            default:
                log.Warn($"[output.\"{name}\"] transform \"{text}\" is not normal|90|180|270|flipped|flipped-90|flipped-180|flipped-270, ignored");
                return null;
        }
    }

    private static (int Width, int Height, int? Refresh)? ParseMode(string text, string name, BasinLogger log)
    {
        var at = text.Split('@');
        var size = at[0].Split('x');
        if (at.Length <= 2 && size.Length == 2 &&
            int.TryParse(size[0], out var width) && int.TryParse(size[1], out var height))
        {
            if (at.Length == 1)
            {
                return (width, height, null);
            }

            if (int.TryParse(at[1], out var refresh))
            {
                return (width, height, refresh);
            }
        }

        log.Warn($"[output.\"{name}\"] mode \"{text}\" is not WIDTHxHEIGHT or WIDTHxHEIGHT@HZ, ignored");
        return null;
    }

    private static string? Animation(string? name) => name is null or "none" ? null : name;

    private static readonly string[] PostNames =
        ["none", "invert", "magnify", "zoom", "color-blindness", "show-paint"];

    private static IReadOnlyList<string> PostStages(TomlReader effects, BasinLogger log)
    {
        var names = effects.Words("post");
        if (names is null)
        {
            return [];
        }

        var chosen = new List<string>(names.Length);
        foreach (var name in names)
        {
            if (name == "none")
            {
                continue;
            }

            if (Array.IndexOf(PostNames, name) < 0)
            {
                log.Warn($"effects.post: unknown stage '{name}', ignored");
                continue;
            }

            if (!chosen.Contains(name))
            {
                chosen.Add(name);
            }
        }

        return chosen;
    }
}
