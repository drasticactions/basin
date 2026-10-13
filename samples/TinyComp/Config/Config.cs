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
        ("Super+comma", "settings"),
    ];

    public string Renderer { get; set; } = "vulkan";

    public HandoffSettings Handoff { get; set; } = new();

    public int Outputs { get; set; } = 1;

    public double[] Scales { get; set; } = [];

    public long Frames { get; set; }

    public bool Transactions { get; set; } = true;

    public bool Offload { get; set; } = true;

    public bool FullRepaint { get; set; }

    public bool DamageTint { get; set; }

    public bool HideCursorOnTouch { get; set; } = true;

    public int CursorHideAfterMs { get; set; }

    public bool CursorParkPointer { get; set; }

    public bool TitlebarTwist { get; set; } = true;

    public int WindowTwistFingers { get; set; } = 3;

    public bool HoldMove { get; set; } = true;

    public int HoldMillis { get; set; } = 300;

    public bool HoldMoveTurn { get; set; } = true;

    public bool QuillDemo { get; set; }

    public static RenderColor DefaultBackground { get; } = new(0.09f, 0.1f, 0.12f, 1f);

    public RenderColor Background { get; set; } = DefaultBackground;

    public FrameStyle FrameStyle { get; set; } = FrameStyle.Flat;

    public string? MetacityTheme { get; set; }

    public string MetacityButtonLayout { get; set; } = "menu:minimize,maximize,close";

    public string MetacityPalette { get; set; } = "light";

    public string QuillPalette { get; set; } = "dark";

    public double QuillCornerRadius { get; set; } = 8;

    public double QuillFontSize { get; set; } = 13;

    public double QuillBackdropBlur { get; set; }

    public double MetacityBackdropBlur { get; set; }

    public double QuillFrostOpacity { get; set; } = 0.44;

    public string? MsStylesTheme { get; set; }

    public string MsStylesColor { get; set; } = string.Empty;

    public string MsStylesSize { get; set; } = string.Empty;

    public string MsStylesLook { get; set; } = "auto";

    public string MsStylesFont { get; set; } = string.Empty;

    public uint MsStylesColorization { get; set; } = Basin.Frames.MsStyles.MsStylesFrameStyle.DefaultColorization;

    public double MsStylesBackdropBlur { get; set; }

    public string MsStylesPainter { get; set; } = "skia";

    public int CornerRadius { get; set; }

    public double FontSize { get; set; } = 14;

    public OutputColorProfileSource ColorSource { get; set; } = OutputColorProfileSource.Edid;

    public string? IccProfile { get; set; }

    public bool Hdr { get; set; }

    public double? NightLight { get; set; }

    public bool InverseToneMapping { get; set; }

    public double ItmSdrNits { get; set; } = 100;

    public double ItmTargetNits { get; set; } = 1000;

    public string? Look { get; set; }

    public bool HdrDemand { get; set; }

    public Basin.Host.VrrPacingPolicy VrrPacing { get; set; } = Basin.Host.VrrPacingPolicy.Fullscreen;

    public bool AllowTearing { get; set; } = true;

    public bool? ProcessSubreaper { get; set; }

    public bool Wobbly { get; set; }

    public string? OpenAnimation { get; set; }

    public string? CloseAnimation { get; set; }

    public bool Switcher { get; set; }

    public string? MinimizeAnimation { get; set; }

    public bool Highlight { get; set; }

    public bool DimInactive { get; set; }

    public bool DropShadow { get; set; }

    public string BlurKernel { get; set; } = "kawase";

    public bool SlideBack { get; set; }

    public bool Stretch { get; set; }

    public bool Notifications { get; set; }

    public bool ShakeCursor { get; set; }

    public Basin.XWayland.XWaylandScale XWaylandScale { get; set; }

    public int[] XWaylandDpiBuckets { get; set; } = [];

    public bool XWaylandLowDensity { get; set; } = true;

    public double RotateStep { get; set; } = 15;

    public bool RotateSpin { get; set; }

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

    public IReadOnlyList<ShaderSetting> Shaders { get; set; } = [];

    public PhysicsSetting Physics { get; set; } = PhysicsSetting.Defaults;

    public bool ShaderContinuous { get; set; }

    private static readonly Dictionary<string, double> EmptyShaderParams = [];

    public HashSet<string> FromFlags { get; } = new(StringComparer.Ordinal);

    public HashSet<string> FromFile { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<Binding> Bindings { get; private set; } = [];

    public string SettingsPalette { get; set; } = "dark";

    public double SettingsFontSize { get; set; } = 14;

    public bool HyprEnabled { get; set; } = true;

    public bool HyprInputCapture { get; set; } = true;

    public bool HyprCtm { get; set; } = true;

    public IReadOnlyDictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> Shortcuts { get; private set; } =
        new Dictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)>();

    public IReadOnlyList<Rule> Rules { get; private set; } = [];

    public LaunchRules Launch { get; internal set; } = LaunchRules.Empty;

    public static IReadOnlyList<InputRule> DefaultInputRules { get; } =
    [
        new InputRule
        {
            Match = new InputMatch(Type: InputDeviceType.Touchpad),
            Settings =
            [
                (InputSetting.Tap, InputSettingValue.Flag(true)),
                (InputSetting.ClickMethod, InputSettingValue.Of(InputClickMethod.Clickfinger)),
            ],
        },
    ];

    public IReadOnlyList<InputRule> InputRules { get; private set; } = DefaultInputRules;

    public IReadOnlyDictionary<string, OutputSetting> OutputSettings { get; private set; } =
        new Dictionary<string, OutputSetting>(StringComparer.Ordinal);

    public OutputSetting? OutputSettingFor(string name) =>
        OutputSettings.TryGetValue(name, out var setting) ? setting : null;

    public CanvasSetting Canvas { get; set; } = CanvasSetting.Defaults;

    public CanvasSetting CanvasFor(string outputName, BasinLogger log) =>
        OutputSettingFor(outputName)?.Canvas is { } over
            ? over.Over(Canvas).Constrained($"output.\"{outputName}\"", log)
            : Canvas;

    public OverviewSetting Overview { get; set; } = OverviewSetting.Defaults;

    public OverviewSetting OverviewFor(string outputName) =>
        OutputSettingFor(outputName)?.Overview is { } over ? over.Over(Overview) : Overview;

    public CanvasSetting OverviewCanvasFor(string outputName, BasinLogger log) =>
        new CanvasSetting { Enable = CanvasFor(outputName, log).Enable }.Over(OverviewFor(outputName).TerraceValue);

    public bool CanvasAnywhere =>
        Canvas.Enabled || OutputSettings.Values.Any(static setting => setting.Canvas?.Enable == true);

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
        "canvas-toggle" => KeyAction.CanvasToggle,
        "park-left" => KeyAction.ParkLeft,
        "park-right" => KeyAction.ParkRight,
        "park-up" => KeyAction.ParkUp,
        "park-down" => KeyAction.ParkDown,
        "recall" => KeyAction.Recall,
        "shelf-smaller" => KeyAction.ShelfSmaller,
        "shelf-larger" => KeyAction.ShelfLarger,
        "shelf-reset" => KeyAction.ShelfReset,
        "canvas-mode" => KeyAction.CanvasMode,
        "overview" => KeyAction.Overview,
        "shelve-left" => KeyAction.ShelveLeft,
        "shelve-right" => KeyAction.ShelveRight,
        "shelve-top" => KeyAction.ShelveTop,
        "shelve-bottom" => KeyAction.ShelveBottom,
        "unshelve" => KeyAction.Unshelve,
        "settings" => KeyAction.Settings,
        "gravity" => KeyAction.Gravity,
        "physics-freeze" => KeyAction.PhysicsFreeze,
        "rotate-left" => KeyAction.RotateLeft,
        "rotate-right" => KeyAction.RotateRight,
        "upright" => KeyAction.Upright,
        "stop-spin" => KeyAction.StopSpin,
        _ => null,
    };

    public static IReadOnlyList<string> ActionNames { get; } =
    [
        "quit", "cycle", "switcher", "cycle-focus", "cycle-scale", "workspace-next", "workspace-prev",
        "carry-next", "carry-prev", "workspace-new", "zoom-in", "zoom-out", "zoom-reset", "mark-undo",
        "mark-clear", "bell", "canvas-toggle", "park-left", "park-right", "park-up", "park-down", "recall",
        "shelf-smaller", "shelf-larger", "shelf-reset", "canvas-mode", "overview", "shelve-left",
        "shelve-right", "shelve-top", "shelve-bottom", "unshelve", "settings", "gravity", "physics-freeze",
        "rotate-left", "rotate-right", "upright", "stop-spin",
    ];

    private static readonly string[] SharedKeys =
        ["renderer", "outputs", "scale", "frames", "offload", "full_repaint", "damage_tint"];

    public static string DefaultPath() => TomlConfig.DefaultPath("tinycomp");

    public static Config Load(string? path, BasinLogger log, out string? fatal)
    {
        var config = new Config();
        config.SeedBindings(log);
        var load = TomlConfig.Load(path == "false" ? null : path, "tinycomp", path == "false", TemplateStream, log);
        fatal = load.Fatal;
        if (load.Table is not { } table)
        {
            return config;
        }

        config.Apply(new TomlReader(table, log));
        fatal = config.ValidateMetacity() ?? config.ValidateMsStyles();
        return config;
    }

    public static Config Parse(string text, BasinLogger log, out string? fatal)
    {
        ArgumentNullException.ThrowIfNull(text);
        var config = new Config();
        config.SeedBindings(log);
        TomlTable table;
        try
        {
            table = Tomlyn.Toml.ToModel(text);
        }
        catch (Tomlyn.TomlException error)
        {
            fatal = error.Message;
            return config;
        }

        config.Apply(new TomlReader(table, log));
        fatal = config.ValidateMetacity() ?? config.ValidateMsStyles();
        return config;
    }

    public string? ValidateMetacity()
    {
        if (FrameStyle != FrameStyle.Metacity)
        {
            return null;
        }

        var installed = Basin.Frames.Metacity.MetacityThemes.Available();
        var available = installed.Count == 0 ? "none installed" : string.Join(", ", installed);
        if (MetacityTheme is not { Length: > 0 } name)
        {
            return $"[frame] style = \"metacity\" names no theme in [frame.metacity] theme; installed themes: {available}";
        }

        try
        {
            _ = Basin.Frames.Metacity.MetacityTheme.Load(name);
            return null;
        }
        catch (Basin.Frames.Metacity.MetacityThemeException e)
        {
            return $"[frame.metacity] theme = \"{name}\": {e.Message}; installed themes: {available}";
        }
    }

    public string? ValidateMsStyles()
    {
        if (FrameStyle != FrameStyle.MsStyles)
        {
            return null;
        }

        if (MsStylesTheme is not { Length: > 0 } name)
        {
            return $"[frame] style = \"msstyles\" names no theme in [frame.msstyles] theme; installed themes: {InstalledMsStyles()}";
        }

        try
        {
            using var frames = MsStylesFrames.Load(this, null);
            return null;
        }
        catch (MsStylesFrameException e)
        {
            return $"[frame.msstyles] theme = \"{name}\": {e.Message}; installed themes: {InstalledMsStyles()}";
        }
    }

    private static string InstalledMsStyles()
    {
        var installed = Basin.Themes.MsStyles.MsStylesThemes.Available();
        return installed.Count == 0 ? "none installed" : string.Join(", ", installed.Select(t => t.Name));
    }

    public static string Template()
    {
        using var reader = new StreamReader(TemplateStream());
        return reader.ReadToEnd();
    }

    private static Stream TemplateStream() =>
        typeof(Config).Assembly.GetManifestResourceStream("tinycomp.toml")
            ?? throw new InvalidOperationException("tinycomp.toml is not embedded");

    public Rule? RuleFor(string? appId, string? title) => RuleFor(new WindowFacts { AppId = appId, Title = title });

    public Rule? RuleFor(in WindowFacts facts)
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
            if (compositor.Text("background") is { } background)
            {
                if (TomlColor.Rgba(background) is { } rgba)
                {
                    Background = new RenderColor(
                        ((rgba >> 24) & 0xff) / 255f, ((rgba >> 16) & 0xff) / 255f, ((rgba >> 8) & 0xff) / 255f, 1f);
                }
                else
                {
                    log.Warn($"[compositor] background \"{background}\" is not #rrggbb, keeping the default");
                }
            }
        }

        if (reader.Section("frame") is { } frame)
        {
            FrameStyle = frame.Choice("style", "flat", "beos", "flat", "metacity", "msstyles", "quill", "none") switch
            {
                "beos" => FrameStyle.Beos,
                "metacity" => FrameStyle.Metacity,
                "msstyles" => FrameStyle.MsStyles,
                "quill" => FrameStyle.Quill,
                "none" => FrameStyle.None,
                _ => FrameStyle.Flat,
            };
            CornerRadius = frame.Number("corner_radius", CornerRadius);
            var fontSize = frame.Number("font_size", FontSize);
            if (fontSize >= 1)
            {
                FontSize = fontSize;
            }
            else
            {
                reader.Log.Warn($"[frame] font_size must be at least 1, keeping {FontSize}");
            }

            if (frame.Section("metacity") is { } metacity)
            {
                MetacityTheme = metacity.Text("theme") ?? MetacityTheme;
                MetacityButtonLayout = metacity.Text("button_layout") ?? MetacityButtonLayout;
                MetacityPalette = metacity.Choice("palette", "light", "light", "dark");
                MetacityBackdropBlur = metacity.Number("backdrop_blur", MetacityBackdropBlur);
            }

            if (frame.Section("msstyles") is { } msstyles)
            {
                MsStylesTheme = msstyles.Text("theme") ?? MsStylesTheme;
                MsStylesColor = msstyles.Text("color") ?? MsStylesColor;
                MsStylesSize = msstyles.Text("size") ?? MsStylesSize;
                MsStylesLook = msstyles.Choice("look", "auto", "auto", "classic-xp", "basic", "glass");
                MsStylesFont = msstyles.Text("font") ?? MsStylesFont;
                if (msstyles.Text("colorization") is { } colorization)
                {
                    if (TomlColor.Rgba(colorization) is not null)
                    {
                        MsStylesColorization = TomlColor.Argb(colorization, MsStylesColorization);
                    }
                    else
                    {
                        reader.Log.Warn($"[frame.msstyles] colorization \"{colorization}\" is not #aarrggbb, keeping the default");
                    }
                }

                MsStylesBackdropBlur = msstyles.Number("backdrop_blur", MsStylesBackdropBlur);
                MsStylesPainter = msstyles.Choice("painter", "skia", "skia", "quill");
            }

            if (frame.Section("quill") is { } quill)
            {
                QuillPalette = quill.Choice("palette", "dark", "dark", "light");
                QuillCornerRadius = quill.Number("corner_radius", QuillCornerRadius);
                QuillFontSize = quill.Number("font_size", QuillFontSize);
                QuillBackdropBlur = quill.Number("backdrop_blur", QuillBackdropBlur);
                QuillFrostOpacity = quill.Number("frost_opacity", QuillFrostOpacity);
            }
        }

        if (reader.Section("color") is { } color)
        {
            foreach (var key in new[] { "source", "icc", "hdr" })
            {
                if (color.Table.ContainsKey(key))
                {
                    FromFile.Add("color." + key);
                }
            }

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
            InverseToneMapping = color.Flag("inverse_tone_mapping", InverseToneMapping);
            ItmSdrNits = color.Number("itm_sdr_nits", ItmSdrNits);
            ItmTargetNits = color.Number("itm_target_nits", ItmTargetNits);
            if (ItmSdrNits <= 0 || ItmTargetNits <= ItmSdrNits)
            {
                log.Warn($"[color] itm_sdr_nits {ItmSdrNits} and itm_target_nits {ItmTargetNits} need 0 < sdr < target, using 100 and 1000");
                (ItmSdrNits, ItmTargetNits) = (100, 1000);
            }

            Look = color.Text("look") is { Length: > 0 } look && look != "none" ? look : null;
            HdrDemand = color.Flag("hdr_demand", HdrDemand);
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
            BlurKernel = effects.Choice("blur_kernel", "kawase", "kawase", "vista");
            SlideBack = effects.Flag("slide_back", SlideBack);
            Stretch = effects.Flag("stretch", Stretch);
            Notifications = effects.Flag("notifications", Notifications);
            ShakeCursor = effects.Flag("shake_cursor", ShakeCursor);
            RotateStep = Math.Clamp(effects.Number("rotate_step", RotateStep), 0.5, 180);
            RotateSpin = effects.Choice("rotate_release", "exact", "exact", "spin") == "spin";
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
            Shaders = ShaderPresets(effects, log);
            ShaderContinuous = effects.Flag("shader_continuous", ShaderContinuous);
        }

        if (reader.Free("canvas") is { } canvas)
        {
            Canvas = CanvasSetting.Parse(canvas, "canvas", log).Over(CanvasSetting.Defaults);
        }

        if (reader.Free("overview") is { } overview)
        {
            Overview = OverviewSetting.Parse(overview, "overview", log).Over(OverviewSetting.Defaults);
            if (Overview.TerraceTable is { } terraceKeys)
            {
                NoteCanvasKeys(terraceKeys);
            }
        }

        if (reader.Section("physics") is { } physics)
        {
            Physics = PhysicsSetting.Parse(physics);
        }

        if (reader.Section("xwayland") is { } xwayland)
        {
            var raw = xwayland.Raw("scale");
            if (Basin.XWayland.XWaylandScale.TryParse(raw, out var scale))
            {
                XWaylandScale = scale;
            }
            else
            {
                log.Warn($"[xwayland] scale {raw} is not \"auto\" or a number of at least 1, keeping {XWaylandScale}");
            }

            if (xwayland.Numbers("dpi_buckets") is { } buckets)
            {
                XWaylandDpiBuckets = [.. buckets.Where(static b => b > 0).Select(static b => (int)Math.Round(b))];
            }

            if (xwayland.Table.ContainsKey("low_density"))
            {
                FromFile.Add("xwayland.low_density");
            }

            XWaylandLowDensity = xwayland.Flag("low_density", XWaylandLowDensity);
        }

        if (reader.Section("cursor") is { } cursor)
        {
            CursorHideAfterMs = Math.Max(0, cursor.Number("hide_after_ms", CursorHideAfterMs));
            CursorParkPointer = cursor.Flag("park_pointer", CursorParkPointer);
        }

        if (reader.Section("touch") is { } touch)
        {
            HideCursorOnTouch = touch.Flag("hide_cursor", HideCursorOnTouch);
            TitlebarTwist = touch.Flag("titlebar_twist", TitlebarTwist);
            var fingers = touch.Number("window_twist", WindowTwistFingers);
            if (fingers is 0 or (>= 3 and <= 5))
            {
                WindowTwistFingers = fingers;
            }
            else
            {
                log.Warn($"[touch] window_twist {fingers} is not 0 or 3 to 5, keeping {WindowTwistFingers}");
            }

            HoldMove = touch.Flag("hold_move", HoldMove);
            HoldMillis = Math.Clamp(touch.Number("hold_ms", HoldMillis), 50, 2000);
            HoldMoveTurn = touch.Flag("hold_move_turn", HoldMoveTurn);
        }

        if (reader.Section("settings") is { } settings)
        {
            SettingsPalette = settings.Choice("palette", "dark", "dark", "light");
            var panelFont = settings.Number("font_size", SettingsFontSize);
            if (panelFont >= 6)
            {
                SettingsFontSize = panelFont;
            }
            else
            {
                log.Warn($"[settings] font_size must be at least 6, keeping {SettingsFontSize}");
            }
        }

        if (reader.Section("hypr") is { } hypr)
        {
            foreach (var key in new[] { "enable", "input_capture", "ctm" })
            {
                if (hypr.Table.ContainsKey(key))
                {
                    FromFile.Add("hypr." + key);
                }
            }

            HyprEnabled = hypr.Flag("enable", HyprEnabled);
            HyprInputCapture = hypr.Flag("input_capture", HyprInputCapture);
            HyprCtm = hypr.Flag("ctm", HyprCtm);
            if (hypr.Free("shortcuts") is { } renamed)
            {
                log.Warn($"[hypr.shortcuts] is now [shortcuts]; the old table is read this once, rename it");
                Shortcuts = ParseShortcuts(renamed, "hypr.shortcuts", log);
            }
        }

        if (reader.Free("shortcuts") is { } shortcuts)
        {
            Shortcuts = ParseShortcuts(shortcuts, "shortcuts", log);
        }

        if (reader.Section("process") is { } process && process.Table.ContainsKey("subreaper"))
        {
            ProcessSubreaper = process.Flag("subreaper", true);
        }

        if (reader.Free("output") is { } outputs)
        {
            var parsed = new Dictionary<string, OutputSetting>(StringComparer.Ordinal);
            foreach (var (name, value) in outputs)
            {
                if (name == "vrr_pacing" && value is string pacing)
                {
                    VrrPacing = pacing switch
                    {
                        "always" => Basin.Host.VrrPacingPolicy.Always,
                        "never" => Basin.Host.VrrPacingPolicy.Never,
                        "fullscreen" => Basin.Host.VrrPacingPolicy.Fullscreen,
                        _ => Warned(log, $"[output] vrr_pacing \"{pacing}\" is not fullscreen, always or never", VrrPacing),
                    };
                }
                else if (name == "allow_tearing" && value is bool tearing)
                {
                    AllowTearing = tearing;
                }
                else if (value is TomlTable table)
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

        if (reader.Section("input") is { } input)
        {
            InputRules = InputConfig.Read(input, log);
        }

        if (reader.FreeArray("rule") is { } rules)
        {
            Rules = WindowRule.MostSpecificFirst(rules.Select(row => ParseRule(row, log)).OfType<Rule>());
        }

        Launch = LaunchRules.Read(reader.FreeArray(LaunchRules.Table), log);

        Handoff = HandoffSettings.Read(reader.Section("handoff"));
        reader.ReportUnknown();
        WarnOverviewAgainstCanvas(log);
    }

    private static T Warned<T>(BasinLogger log, string message, T kept)
    {
        log.Warn($"{message}");
        return kept;
    }

    private static readonly string[] StepIgnoredKeys =
        ["shelf", "slope", "slope_window", "shelf_shape", "corner", "corner_radius", "mesh_cell", "drag"];

    private readonly SortedSet<string> _canvasKeys = new(StringComparer.Ordinal);

    private void NoteCanvasKeys(TomlTable table)
    {
        foreach (var (key, value) in table)
        {
            if (key != "drag" || value is "grid")
            {
                _canvasKeys.Add(key);
            }
        }
    }

    public bool StepsAnywhere =>
        Overview.Steps || OutputSettings.Values.Any(setting => (setting.Overview?.Wall ?? Overview.WallValue) == OverviewWall.Step);

    private void WarnStepIgnoredKeys(BasinLogger log)
    {
        foreach (var setting in OutputSettings.Values)
        {
            if (setting.Overview?.TerraceTable is { } keys)
            {
                NoteCanvasKeys(keys);
            }
        }

        if (!Overview.Enabled || !StepsAnywhere)
        {
            return;
        }

        var ignored = StepIgnoredKeys.Where(_canvasKeys.Contains).Select(key => key == "drag" ? "drag = \"grid\"" : key).ToArray();
        if (ignored.Length > 0)
        {
            log.Warn($"[overview] wall = \"step\" ignores {string.Join(", ", ignored)}");
        }
    }

    public bool SlopeAnywhere =>
        !Overview.Steps || OutputSettings.Values.Any(setting => (setting.Overview?.Wall ?? Overview.WallValue) == OverviewWall.Slope);

    private void WarnOverviewAgainstCanvas(BasinLogger log)
    {
        WarnStepIgnoredKeys(log);
        var stepPinch = Overview.TerraceValue.GridEffectsValue.PinchValue;
        if (Overview.Enabled && StepsAnywhere && (stepPinch < 0 || stepPinch > Basin.Effects.CanvasStepMap.MaxBulge))
        {
            log.Warn($"[overview.grid_effects] pinch {stepPinch} is outside 0 to {Basin.Effects.CanvasStepMap.MaxBulge} on the step overview, clamping it there");
        }

        if (Overview.Enabled && Overview.Textured && SlopeAnywhere)
        {
            log.Warn($"[overview] wall_texture and shelf_texture apply only to wall = \"step\"");
        }

        if (Overview.Enabled && CanvasAnywhere)
        {
            log.Warn($"[overview] is on and so is [canvas] enable: overview is off on every output with the canvas enabled");
        }

        var scale = Overview.ScaleValue;
        var scales = Overview.TerraceValue.ShelfScaleValues;
        var over = CanvasSide.None;
        foreach (var side in CanvasSides.Each)
        {
            if (scales.For(side) > scale)
            {
                over |= side;
            }
        }

        if (Overview.Enabled && over != CanvasSide.None)
        {
            log.Warn($"[overview] shelf_scale on {CanvasSetting.NamesOf(over)} is above scale {scale:F2}: overview clamps it to {scale:F2}");
        }
    }

    private static IReadOnlyDictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> ParseShortcuts(
        TomlTable table, string section, BasinLogger log)
    {
        var rows = new Dictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)>();
        foreach (var (name, value) in table)
        {
            var colon = name.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == name.Length - 1)
            {
                log.Warn($"[{section}] \"{name}\" is not app_id:id, ignored");
                continue;
            }

            if (value is not string chord || !HotkeyParser.TryParseChord(chord, log, out var keysym, out var modifiers))
            {
                log.Warn($"[{section}] \"{name}\" names no chord, ignored");
                continue;
            }

            rows[(name[..colon], name[(colon + 1)..])] = (keysym, modifiers);
        }

        return rows;
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
                hotkey.Command) { When = hotkey.When });
        }

        return merged;
    }

    private static Rule? ParseRule(TomlTable table, BasinLogger log)
    {
        var appIds = WindowRule.Strings(table, "app_id");
        var titleRegex = WindowRule.Pattern(table, "title_regex", log);
        var steamGames = WindowRule.SteamGameIds(table, "steam_game", log);
        var processes = WindowRule.Strings(table, "process");
        var x11Classes = WindowRule.Strings(table, "x11_class");
        var x11Instances = WindowRule.Strings(table, "x11_instance");
        var windowTypes = WindowRule.Strings(table, "window_type");
        bool? wantsFocus = table.TryGetValue("wants_focus", out var wantsFocusValue) && wantsFocusValue is bool wants ? wants : null;
        bool? skipTaskbar = table.TryGetValue("skip_taskbar", out var skipTaskbarValue) && skipTaskbarValue is bool skips ? skips : null;
        if (appIds is null && titleRegex is null && steamGames is null && processes is null
            && x11Classes is null && x11Instances is null && windowTypes is null && wantsFocus is null && skipTaskbar is null)
        {
            log.Warn($"a [[rule]] naming no match key is dropped");
            return null;
        }

        int? Number(string key) =>
            table.TryGetValue(key, out var value) && value is long number ? (int)number : null;

        string? Text(string key) =>
            table.TryGetValue(key, out var value) && value is string { Length: > 0 } text ? text : null;

        bool? Flag(string key) =>
            table.TryGetValue(key, out var value) && value is bool flag ? flag : null;

        double? Real(string key) =>
            table.TryGetValue(key, out var value) ? value switch { double real => real, long whole => whole, _ => null } : null;

        return new Rule
        {
            AppIds = appIds,
            TitleRegex = titleRegex,
            SteamGames = steamGames,
            Processes = processes,
            X11Classes = x11Classes,
            X11Instances = x11Instances,
            WindowTypes = windowTypes,
            WantsFocus = wantsFocus,
            SkipTaskbar = skipTaskbar,
            FrameStyle = Text("frame") switch
            {
                "beos" => global::TinyComp.FrameStyle.Beos,
                "flat" => global::TinyComp.FrameStyle.Flat,
                "metacity" => global::TinyComp.FrameStyle.Metacity,
                "msstyles" => global::TinyComp.FrameStyle.MsStyles,
                "quill" => global::TinyComp.FrameStyle.Quill,
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
            Physics = Flag("physics"),
            Mass = Real("mass"),
            Gravity = Real("gravity"),
            Bounce = Real("bounce"),
            Friction = Real("friction"),
            Pin = Flag("pin"),
            NoCollide = Flag("no_collide"),
            Quirks = WindowQuirks.Read(table, log),
            Canvas = Text("canvas") switch
            {
                "left" => CanvasSide.Left,
                "right" => CanvasSide.Right,
                "top" => CanvasSide.Top,
                "bottom" => CanvasSide.Bottom,
                "top-left" => CanvasSide.Top | CanvasSide.Left,
                "top-right" => CanvasSide.Top | CanvasSide.Right,
                "bottom-left" => CanvasSide.Bottom | CanvasSide.Left,
                "bottom-right" => CanvasSide.Bottom | CanvasSide.Right,
                _ => null,
            },
        };
    }

    private static OverviewSetting? OutputOverview(
        string name, TomlTable? keys, bool? enable, double? scale, OverviewWall? wall, BasinLogger log)
    {
        if (keys is null)
        {
            return enable is null && scale is null && wall is null
                ? null
                : new OverviewSetting { Enable = enable, Scale = scale, Wall = wall };
        }

        var section = $"output.\"{name}\".overview";
        var accepted = new TomlTable();
        foreach (var (key, value) in keys)
        {
            if (key is "enable" or "scale" or "wall" or "anchor" || Array.IndexOf(OverviewSetting.TerraceKeys, key) >= 0)
            {
                accepted[key] = value;
            }
            else
            {
                log.Warn($"[{section}] {key}: not an output key, ignored");
            }
        }

        var parsed = OverviewSetting.Parse(accepted, section, log);
        return new OverviewSetting
        {
            Enable = parsed.Enable,
            Scale = scale ?? parsed.Scale,
            Wall = wall ?? parsed.Wall,
            Anchor = parsed.Anchor,
            Terrace = parsed.Terrace,
            TerraceTable = parsed.TerraceTable,
        };
    }

    private static OutputSetting ParseOutputSetting(string name, TomlTable table, BasinLogger log)
    {
        double? scale = null;
        OutputTransform? transform = null;
        (int Width, int Height, int? Refresh)? mode = null;
        TomlTable? canvasKeys = null;
        bool? overview = null;
        double? overviewScale = null;
        OverviewWall? overviewWall = null;
        TomlTable? overviewKeys = null;
        foreach (var (key, value) in table)
        {
            switch (key)
            {
                case "enable" or "zone" or "extension" or "edge_scale" or "slope" or "mesh_cell"
                    or "grid" or "grid_cell" or "grid_color" or "desktop_grid"
                    or "grid_animation" or "grid_animation_ms" or "grid_easing" or "grid_linger_ms" or "wallpaper" or "animation_ms" or "sides" or "corner" or "corner_radius"
                    or "window" or "min_scale" or "scale_reach" or "shelf" or "shelf_scale" or "shelf_min_scale"
                    or "shelf_step" or "shelf_shape" or "slope_window"
                    or "drag" or "grid_effects":
                    (canvasKeys ??= [])[key] = value;
                    break;
                case "overview" when value is bool overviewFlag:
                    overview = overviewFlag;
                    break;
                case "overview" when value is TomlTable overviewTable:
                    overviewKeys = overviewTable;
                    break;
                case "overview_wall" when value is string wallName:
                    overviewWall = OverviewSetting.WallFromName(wallName);
                    if (overviewWall is null)
                    {
                        log.Warn($"[output.\"{name}\"] overview_wall \"{wallName}\" is not slope|step, ignored");
                    }

                    break;
                case "overview_scale" when value is double or long:
                    overviewScale = Math.Clamp(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), OverviewSetting.MinScale, OverviewSetting.MaxScale);
                    break;
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

        return new OutputSetting
        {
            Scale = scale,
            Transform = transform,
            Mode = mode,
            Canvas = canvasKeys is null ? null : CanvasSetting.Parse(canvasKeys, $"output.\"{name}\"", log),
            Overview = OutputOverview(name, overviewKeys, overview, overviewScale, overviewWall, log),
            CanvasKeys = canvasKeys,
        };
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

    private static IReadOnlyList<ShaderSetting> ShaderPresets(TomlReader effects, BasinLogger log)
    {
        _ = effects.Table.TryGetValue("shader", out var raw);
        if (raw is TomlTableArray)
        {
            var entries = new List<ShaderSetting>();
            foreach (var row in effects.Sections("shader"))
            {
                var path = row.Text("path");
                var parameters = ShaderParameters(row, "params");
                if (string.IsNullOrWhiteSpace(path))
                {
                    log.Warn($"effects.shader: an entry names no path, ignored");
                    continue;
                }

                entries.Add(new ShaderSetting(path, parameters));
            }

            if (ShaderParameters(effects, "shader_params").Count > 0)
            {
                log.Warn($"effects.shader_params is ignored with [[effects.shader]]; put params on each entry");
            }

            return entries;
        }

        var text = effects.Text("shader");
        var single = string.IsNullOrWhiteSpace(text) || text is "none" or "false" ? null : text;
        var singleParameters = ShaderParameters(effects, "shader_params");
        return single is null ? [] : [new ShaderSetting(single, singleParameters)];
    }

    private static IReadOnlyDictionary<string, double> ShaderParameters(TomlReader reader, string key)
    {
        if (reader.Section(key) is not { } table)
        {
            return EmptyShaderParams;
        }

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var name in table.Table.Keys.ToArray())
        {
            values[name] = table.Number(name, 0.0);
        }

        return values;
    }
}
