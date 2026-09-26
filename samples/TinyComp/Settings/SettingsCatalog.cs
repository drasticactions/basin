namespace TinyComp;

internal static class SettingsCatalog
{
    public const string Compositor = "Compositor";
    public const string Frames = "Frames";
    public const string Color = "Color";
    public const string Effects = "Effects";
    public const string Canvas = "Canvas";
    public const string Overview = "Overview";
    public const string Hypr = "Hypr";
    public const string Bindings = "Bindings";
    public const string Rules = "Rules";
    public const string Outputs = "Outputs";
    public const string Panel = "Panel";

    public static IReadOnlyList<string> Sections { get; } =
        [Compositor, Frames, Color, Effects, Canvas, Overview, Hypr, Bindings, Rules, Outputs, Panel];

    public static IReadOnlyList<string> Restarts { get; } =
    [
        "compositor.renderer", "compositor.outputs", "compositor.frames", "compositor.full_repaint",
        "color.source", "color.icc", "color.hdr", "hypr.enable", "hypr.input_capture", "hypr.ctm",
    ];

    private static readonly string[] TexturePresets = ["none", "stone", "brick", "wood", "noise"];

    private static readonly string[] Animations = ["none", "fade", "zoom", "glide", "sheet"];

    private static readonly string[] Closings = ["none", "fade", "zoom", "fire", "fire-gpu", "glide", "sheet", "fall-apart"];

    public static IReadOnlyList<string> FrameStyles { get; } = ["beos", "flat", "metacity", "quill", "none"];

    public static IReadOnlyList<string> PostStages { get; } = ["invert", "magnify", "zoom", "color-blindness", "show-paint"];

    public static IReadOnlyList<string> Sides { get; } = ["left", "right", "top", "bottom"];

    public static IReadOnlyList<string> Transforms { get; } =
        ["normal", "90", "180", "270", "flipped", "flipped-90", "flipped-180", "flipped-270"];

    public static IReadOnlyList<SettingKey> Keys { get; } = Build();

    public static SettingKey? Find(string path)
    {
        foreach (var key in Keys)
        {
            if (string.Equals(key.Path, path, StringComparison.Ordinal))
            {
                return key;
            }
        }

        return null;
    }

    private static SettingKey[] Build()
    {
        static bool Style(SettingsDraft draft, string style) =>
            string.Equals(draft.TextOf("frame", "style", "flat"), style, StringComparison.Ordinal);

        return
        [
            new(Compositor, "compositor", "renderer", "Renderer", SettingKind.Choice)
            {
                Default = "\"vulkan\"", ChoicesFrom = "renderers", Restart = true, FlagKey = "renderer", Flag = "--renderer",
            },
            new(Compositor, "compositor", "outputs", "Outputs", SettingKind.Integer)
            {
                Default = "1", Min = 1, Max = 8, Restart = true, FlagKey = "outputs", Flag = "--outputs",
            },
            new(Compositor, "compositor", "scale", "Scale per output", SettingKind.Reals)
            {
                Default = "[]", Min = 0.5, Max = 4, FlagKey = "scale", Flag = "--scale",
                Note = "An empty list asks each output for its scale.",
            },
            new(Compositor, "compositor", "background", "Background", SettingKind.Color) { Default = "\"#171a1f\"" },
            new(Compositor, "compositor", "transactions", "Transactions", SettingKind.Flag) { Default = "true" },
            new(Compositor, "compositor", "offload", "Plane offload", SettingKind.Flag)
            {
                Default = "true", FlagKey = "offload", Flag = "--offload",
            },
            new(Compositor, "compositor", "full_repaint", "Full repaint", SettingKind.Flag)
            {
                Default = "false", Restart = true, FlagKey = "full_repaint", Flag = "--full-repaint",
            },
            new(Compositor, "compositor", "damage_tint", "Damage tint", SettingKind.Flag)
            {
                Default = "false", FlagKey = "damage_tint", Flag = "--damage-tint",
            },

            new(Frames, "frame", "style", "Style", SettingKind.Choice) { Default = "\"flat\"", Choices = FrameStyles },
            new(Frames, "frame", "corner_radius", "Corner radius", SettingKind.Integer) { Default = "0", Min = 0, Max = 32 },
            new(Frames, "frame", "font_size", "Title size", SettingKind.Real) { Default = "14", Min = 6, Max = 32, Step = 1 },
            new(Frames, "frame.metacity", "theme", "Theme", SettingKind.Choice)
            {
                ChoicesFrom = "metacity-themes", Group = "Metacity", Visible = static d => Style(d, "metacity"),
            },
            new(Frames, "frame.metacity", "button_layout", "Button layout", SettingKind.Text)
            {
                Default = "\"menu:minimize,maximize,close\"", Group = "Metacity", Visible = static d => Style(d, "metacity"),
            },
            new(Frames, "frame.metacity", "palette", "Palette", SettingKind.Choice)
            {
                Default = "\"light\"", Choices = ["light", "dark"], Group = "Metacity", Visible = static d => Style(d, "metacity"),
            },
            new(Frames, "frame.metacity", "backdrop_blur", "Backdrop blur", SettingKind.Real)
            {
                Default = "0", Min = 0, Max = 15, Step = 1, Group = "Metacity", Visible = static d => Style(d, "metacity"),
            },
            new(Frames, "frame.quill", "palette", "Palette", SettingKind.Choice)
            {
                Default = "\"dark\"", Choices = ["dark", "light"], Group = "Quill", Visible = static d => Style(d, "quill"),
            },
            new(Frames, "frame.quill", "corner_radius", "Corner radius", SettingKind.Real)
            {
                Default = "8", Min = 0, Max = 24, Step = 1, Group = "Quill", Visible = static d => Style(d, "quill"),
            },
            new(Frames, "frame.quill", "font_size", "Title size", SettingKind.Real)
            {
                Default = "13", Min = 6, Max = 32, Step = 1, Group = "Quill", Visible = static d => Style(d, "quill"),
            },
            new(Frames, "frame.quill", "backdrop_blur", "Backdrop blur", SettingKind.Real)
            {
                Default = "0", Min = 0, Max = 15, Step = 1, Group = "Quill", Visible = static d => Style(d, "quill"),
            },
            new(Frames, "frame.quill", "frost_opacity", "Frost opacity", SettingKind.Real)
            {
                Default = "0.44", Min = 0, Max = 1, Step = 0.01, Group = "Quill", Visible = static d => Style(d, "quill"),
            },

            new(Color, "color", "source", "Source", SettingKind.Choice)
            {
                Default = "\"edid\"", Choices = ["edid", "srgb", "icc"], Restart = true,
            },
            new(Color, "color", "icc", "ICC profile", SettingKind.Path) { Default = "\"\"", Restart = true },
            new(Color, "color", "hdr", "HDR", SettingKind.Flag) { Default = "false", Restart = true },
            new(Color, "color", "night_light", "Night light (kelvin, 0 is off)", SettingKind.Real)
            {
                Default = "0", Min = 0, Max = 6500, Step = 100,
            },

            Flag(Effects, "effects", "wobbly", "Wobbly windows", "Window animations"),
            new(Effects, "effects", "open", "Open", SettingKind.Choice) { Default = "\"none\"", Choices = Animations, Group = "Window animations" },
            new(Effects, "effects", "close", "Close", SettingKind.Choice) { Default = "\"none\"", Choices = Closings, Group = "Window animations" },
            new(Effects, "effects", "minimize", "Minimize", SettingKind.Choice)
            {
                Default = "\"none\"", Choices = ["none", "magic-lamp", "squash"], Group = "Window animations",
            },
            Flag(Effects, "effects", "switcher", "3D switcher", "Window animations"),
            Flag(Effects, "effects", "stretch", "Stretch while resizing", "Window animations"),
            Flag(Effects, "effects", "slide_back", "Slide back", "Window animations"),
            Flag(Effects, "effects", "notifications", "Notification slide", "Window animations"),
            Flag(Effects, "effects", "dim_inactive", "Dim inactive windows", "Window appearance"),
            Flag(Effects, "effects", "drop_shadow", "Drop shadows", "Window appearance"),
            Flag(Effects, "effects", "highlight", "Switcher highlight", "Window appearance"),
            new(Effects, "effects", "post", "Post stages", SettingKind.Names)
            {
                Default = "\"none\"", Choices = PostStages, Group = "Post stages",
            },
            new(Effects, "effects", "color_blindness", "Color blindness", SettingKind.Choice)
            {
                Default = "\"protanopia\"", Choices = ["protanopia", "deuteranopia", "tritanopia", "monochrome"], Group = "Post stages",
            },
            new(Effects, "effects", "color_blindness_intensity", "Intensity", SettingKind.Real)
            {
                Default = "1.0", Min = 0, Max = 1, Step = 0.05, Group = "Post stages",
            },
            new(Effects, "effects", "zoom_tracking", "Zoom tracking", SettingKind.Choice)
            {
                Default = "\"proportional\"", Choices = ["proportional", "centered", "push", "disabled", "centered-strict"], Group = "Post stages",
            },
            Flag(Effects, "effects", "blend_changes", "Blend changes", "Post stages"),
            Flag(Effects, "effects", "screen_transform", "Crossfade rotations", "Post stages"),
            new(Effects, "effects", "shader", "Shader preset", SettingKind.Path) { Default = "\"none\"", Group = "Shader" },
            Flag(Effects, "effects", "shader_continuous", "Repaint every vblank", "Shader"),
            Flag(Effects, "effects", "shake_cursor", "Shake to grow the cursor", "Feedback"),
            Flag(Effects, "effects", "mouse_click", "Click rings", "Feedback"),
            Flag(Effects, "effects", "mouse_mark", "Mouse mark", "Feedback"),
            Flag(Effects, "effects", "track_mouse", "Track mouse", "Feedback"),
            Flag(Effects, "effects", "touch_points", "Touch points", "Feedback"),
            Flag(Effects, "effects", "system_bell", "Visual bell", "Feedback"),
            new(Effects, "effects", "startup_feedback", "Startup feedback", SettingKind.Choice)
            {
                Default = "\"none\"", Choices = ["none", "bouncing", "blinking", "passive"], Group = "Feedback",
            },

            new(Canvas, "canvas", "enable", "Canvas", SettingKind.Flag) { Default = "false" },
            new(Canvas, "canvas", "sides", "Sides", SettingKind.Names) { Default = "[\"left\", \"right\"]", Choices = Sides },
            new(Canvas, "canvas", "corner", "Corner", SettingKind.Choice) { Default = "\"round\"", Choices = ["round", "square", "taper"] },
            new(Canvas, "canvas", "corner_radius", "Corner radius", SettingKind.OptionalReal)
            {
                Optional = true, Min = 0, Max = 1, Step = 0.05, Suggested = 1.0,
                Note = "0 is square and 1 is round. Automatic follows the corner shape.",
            },
            new(Canvas, "canvas", "zone", "Zone", SettingKind.OptionalReal)
            {
                Optional = true, Min = 0.02, Max = 0.45, Step = 0.01, Suggested = 0.12,
                Note = "The share of the output each zone takes. Automatic is 0.12, or 0.08 in terrace mode.",
            },
            new(Canvas, "canvas", "extension", "Extension", SettingKind.Real) { Default = "0.5", Min = 0.05, Max = 2, Step = 0.05 },
            new(Canvas, "canvas", "edge_scale", "Edge scale", SettingKind.Real) { Default = "0.2", Min = 0.01, Max = 1, Step = 0.01 },
            new(Canvas, "canvas", "slope", "Slope", SettingKind.Real) { Default = "0.25", Min = -1, Max = 1, Step = 0.05 },
            new(Canvas, "canvas", "mesh_cell", "Mesh cell", SettingKind.Integer) { Default = "16", Min = 4, Max = 128 },
            new(Canvas, "canvas", "grid", "Grid", SettingKind.Choice) { Default = "\"always\"", Choices = ["always", "drag", "never"] },
            new(Canvas, "canvas", "grid_cell", "Grid cell", SettingKind.Integer) { Default = "64", Min = 8, Max = 512 },
            new(Canvas, "canvas", "grid_color", "Grid color", SettingKind.Color) { Default = "\"#2a35c0\"" },
            new(Canvas, "canvas", "desktop_grid", "Grid on the desktop", SettingKind.Flag) { Default = "false" },
            new(Canvas, "canvas", "wallpaper", "Wallpaper", SettingKind.Choice) { Default = "\"desktop\"", Choices = ["desktop", "output"] },
            new(Canvas, "canvas", "animation_ms", "Animation (ms)", SettingKind.Integer) { Default = "250", Min = 0, Max = 5000 },
            new(Canvas, "canvas", "window", "Window mode", SettingKind.Choice) { Default = "\"warp\"", Choices = ["warp", "scale", "terrace"] },
            new(Canvas, "canvas", "min_scale", "Min scale", SettingKind.Real) { Default = "0.35", Min = 0.05, Max = 1, Step = 0.05 },
            new(Canvas, "canvas", "scale_reach", "Scale reach", SettingKind.Real) { Default = "1.0", Min = 0.25, Max = 4, Step = 0.05 },
            new(Canvas, "canvas", "shelf", "Shelf", SettingKind.Real) { Default = "0.10", Min = 0, Max = 0.5, Step = 0.01 },
            new(Canvas, "canvas", "shelf_scale", "Shelf scale", SettingKind.PerSide)
            {
                Default = "0.4", Min = 0.05, Max = 1, Step = 0.05,
            },
            new(Canvas, "canvas", "shelf_min_scale", "Shelf min scale", SettingKind.Real) { Default = "0.2", Min = 0.05, Max = 1, Step = 0.05 },
            new(Canvas, "canvas", "shelf_step", "Shelf step", SettingKind.Real) { Default = "0.05", Min = 0.01, Max = 0.5, Step = 0.01 },
            new(Canvas, "canvas", "shelf_shape", "Shelf shape", SettingKind.Choice) { Default = "\"square\"", Choices = ["square", "flat"] },
            new(Canvas, "canvas", "slope_window", "Window on the slope", SettingKind.Choice) { Default = "\"bend\"", Choices = ["bend", "flat"] },
            new(Canvas, "canvas", "drag", "Drag", SettingKind.Choice) { Default = "\"cursor\"", Choices = ["cursor", "grid"] },

            new(Overview, "overview", "enable", "Overview", SettingKind.Flag) { Default = "true" },
            new(Overview, "overview", "scale", "Scale", SettingKind.Real) { Default = "0.75", Min = 0.3, Max = 0.95, Step = 0.05 },
            new(Overview, "overview", "threshold_in", "Threshold in", SettingKind.Real) { Default = "0.667", Min = 0, Max = 1, Step = 0.01 },
            new(Overview, "overview", "threshold_out", "Threshold out", SettingKind.Real) { Default = "0.333", Min = 0, Max = 1, Step = 0.01 },
            new(Overview, "overview", "gesture", "Gesture", SettingKind.Flag) { Default = "true" },
            new(Overview, "overview", "gesture_fingers", "Gesture fingers", SettingKind.Integer) { Default = "4", Min = 4, Max = 5 },
            new(Overview, "overview", "hot_corner", "Hot corner", SettingKind.Choice)
            {
                Default = "\"top-left\"", Choices = ["top-left", "top-right", "bottom-left", "bottom-right", "none"],
            },
            new(Overview, "overview", "hot_corner_ms", "Hot corner delay (ms)", SettingKind.Integer) { Default = "150", Min = 0, Max = 2000 },
            new(Overview, "overview", "wall", "Wall", SettingKind.Choice) { Default = "\"slope\"", Choices = ["slope", "step"] },
            new(Overview, "overview", "anchor", "Anchor", SettingKind.Choice) { Default = "\"fill\"", Choices = ["fill", "center"] },
            new(Overview, "overview", "wall_width", "Wall width", SettingKind.Real) { Default = "0.04", Min = 0, Max = 0.2, Step = 0.01 },
            new(Overview, "overview", "wall_color", "Wall color", SettingKind.Color) { Default = "\"#262a3a\"" },
            new(Overview, "overview", "wall_shade", "Wall shade", SettingKind.Real) { Default = "0.25", Min = 0, Max = 1, Step = 0.05 },
            new(Overview, "overview", "wall_texture", "Wall texture", SettingKind.Path) { Default = "\"none\"", Presets = TexturePresets },
            new(Overview, "overview", "shelf_texture", "Shelf texture", SettingKind.Path) { Default = "\"none\"", Presets = TexturePresets },
            new(Overview, "overview", "texture_scale", "Texture scale", SettingKind.Real) { Default = "1.0", Min = 0.25, Max = 8, Step = 0.25 },
            new(Overview, "overview", "shelf_color", "Shelf color", SettingKind.Color) { Default = "\"#3a3d44\"" },
            new(Overview, "overview", "texture_grid", "Grid on textures", SettingKind.Flag) { Default = "true" },
            new(Overview, "overview", "sides", "Sides", SettingKind.Names) { Default = "[\"left\", \"right\"]", Choices = Sides },
            new(Overview, "overview", "shelf", "Shelf", SettingKind.Real) { Default = "0.10", Min = 0, Max = 0.5, Step = 0.01 },
            new(Overview, "overview", "shelf_scale", "Shelf scale", SettingKind.PerSide)
            {
                Default = "0.4", Min = 0.05, Max = 1, Step = 0.05,
            },
            new(Overview, "overview", "shelf_min_scale", "Shelf min scale", SettingKind.Real) { Default = "0.2", Min = 0.05, Max = 1, Step = 0.05 },
            new(Overview, "overview", "shelf_step", "Shelf step", SettingKind.Real) { Default = "0.05", Min = 0.01, Max = 0.5, Step = 0.01 },
            new(Overview, "overview", "slope", "Slope", SettingKind.Real) { Default = "0.25", Min = -1, Max = 1, Step = 0.05 },
            new(Overview, "overview", "slope_window", "Window on the slope", SettingKind.Choice) { Default = "\"bend\"", Choices = ["bend", "flat"] },
            new(Overview, "overview", "shelf_shape", "Shelf shape", SettingKind.Choice) { Default = "\"square\"", Choices = ["square", "flat"] },
            new(Overview, "overview", "corner", "Corner", SettingKind.Choice) { Default = "\"round\"", Choices = ["round", "square", "taper"] },
            new(Overview, "overview", "corner_radius", "Corner radius", SettingKind.OptionalReal)
            {
                Optional = true, Min = 0, Max = 1, Step = 0.05, Suggested = 1.0,
                Note = "0 is square and 1 is round. Automatic follows the corner shape.",
            },
            new(Overview, "overview", "mesh_cell", "Mesh cell", SettingKind.Integer) { Default = "16", Min = 4, Max = 128 },
            new(Overview, "overview", "drag", "Drag", SettingKind.Choice) { Default = "\"cursor\"", Choices = ["cursor", "grid"] },
            new(Overview, "overview", "grid", "Grid", SettingKind.Choice) { Default = "\"always\"", Choices = ["always", "drag", "never"] },
            new(Overview, "overview", "grid_cell", "Grid cell", SettingKind.Integer) { Default = "64", Min = 8, Max = 512 },
            new(Overview, "overview", "grid_color", "Grid color", SettingKind.Color) { Default = "\"#2a35c0\"" },
            new(Overview, "overview", "desktop_grid", "Grid on the desktop", SettingKind.Flag) { Default = "false" },
            new(Overview, "overview", "animation_ms", "Animation (ms)", SettingKind.Integer) { Default = "250", Min = 0, Max = 5000 },

            new(Hypr, "hypr", "enable", "hyprland-protocols", SettingKind.Flag) { Default = "true", Restart = true },
            new(Hypr, "hypr", "input_capture", "Input capture", SettingKind.Flag) { Default = "true", Restart = true },
            new(Hypr, "hypr", "ctm", "CTM control", SettingKind.Flag) { Default = "true", Restart = true },

            new(Panel, "settings", "palette", "Palette", SettingKind.Choice) { Default = "\"dark\"", Choices = ["dark", "light"] },
            new(Panel, "settings", "font_size", "Font size", SettingKind.Real) { Default = "14", Min = 10, Max = 24, Step = 1 },
        ];
    }

    private static SettingKey Flag(string section, string table, string key, string label, string group) =>
        new(section, table, key, label, SettingKind.Flag) { Default = "false", Group = group };
}
