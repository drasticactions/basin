namespace TinyComp;

internal static class SettingsCatalog
{
    public const string Compositor = "Compositor";
    public const string Frames = "Frames";
    public const string Color = "Color";
    public const string Effects = "Effects";
    public const string Canvas = "Canvas";
    public const string Overview = "Overview";
    public const string Physics = "Physics";
    public const string Hypr = "Hypr";
    public const string Input = "Input";
    public const string Touch = "Touch";
    public const string Bindings = "Bindings";
    public const string Rules = "Rules";
    public const string Launch = "Launch";
    public const string Outputs = "Outputs";
    public const string Panel = "Panel";

    public static IReadOnlyList<string> Sections { get; } =
        [Compositor, Frames, Color, Effects, Canvas, Overview, Physics, Hypr, Input, Touch, Bindings, Rules, Launch, Outputs, Panel];

    public static IReadOnlyList<string> Restarts { get; } =
    [
        "compositor.renderer", "compositor.outputs", "compositor.frames", "compositor.full_repaint",
        "color.source", "color.icc", "color.hdr", "hypr.enable", "hypr.input_capture", "hypr.ctm",
        "xwayland.low_density",
    ];

    private static readonly string[] TexturePresets = ["none", "stone", "brick", "wood", "noise"];

    private static readonly string[] Animations = ["none", "fade", "zoom", "glide", "sheet"];

    private static readonly string[] Closings = ["none", "fade", "zoom", "fire", "fire-gpu", "glide", "sheet", "fall-apart"];

    public static IReadOnlyList<string> FrameStyles { get; } = ["beos", "flat", "metacity", "msstyles", "quill", "none"];

    public static IReadOnlyList<string> PostStages { get; } = ["invert", "magnify", "zoom", "color-blindness", "show-paint"];

    public static IReadOnlyList<string> Sides { get; } = ["left", "right", "top", "bottom"];

    public static IReadOnlyList<string> GridAnimations { get; } = ["none", "fade", "ripple", "draw", "sweep"];

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
            new(Frames, "frame.msstyles", "theme", "Theme", SettingKind.Choice)
            {
                ChoicesFrom = "msstyles-themes", Group = "Visual style", Visible = static d => Style(d, "msstyles"),
            },
            new(Frames, "frame.msstyles", "color", "Color scheme", SettingKind.Choice)
            {
                Default = "\"\"", ChoicesFrom = "msstyles-colors", Group = "Visual style", Visible = static d => Style(d, "msstyles"),
            },
            new(Frames, "frame.msstyles", "size", "Size", SettingKind.Choice)
            {
                Default = "\"\"", ChoicesFrom = "msstyles-sizes", Group = "Visual style", Visible = static d => Style(d, "msstyles"),
            },
            new(Frames, "frame.msstyles", "look", "Look", SettingKind.Choice)
            {
                Default = "\"auto\"", Choices = ["auto", "classic-xp", "basic", "glass"], Group = "Visual style", Visible = static d => Style(d, "msstyles"),
            },
            new(Frames, "frame.msstyles", "font", "Caption font", SettingKind.Text)
            {
                Default = "\"\"", Group = "Visual style", Visible = static d => Style(d, "msstyles"),
                Note = "Used when the theme's caption font is not installed.",
            },
            new(Frames, "frame.msstyles", "colorization", "Glass color", SettingKind.Color)
            {
                Default = "\"#45409efe\"", Alpha = true, Group = "Visual style", Visible = static d => Style(d, "msstyles"),
                Note = "#aarrggbb: the alpha is the strength of the tint.",
            },
            new(Frames, "frame.msstyles", "backdrop_blur", "Backdrop blur", SettingKind.Real)
            {
                Default = "0", Min = 0, Max = 15, Step = 1, Group = "Visual style", Visible = static d => Style(d, "msstyles"),
            },
            new(Frames, "frame.msstyles", "painter", "Painter", SettingKind.Choice)
            {
                Default = "\"skia\"", Choices = ["skia", "quill"], Group = "Visual style", Visible = static d => Style(d, "msstyles"),
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
            new(Effects, "effects", "blur_kernel", "Client blur", SettingKind.Choice)
            {
                Default = "\"kawase\"", Choices = ["kawase", "vista", "smol"], Group = "Window appearance",
                Note = "vista is Aero glass's blur, which has no strength of its own. smol is a Gaussian as wide as the strength.",
            },
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
            new(Effects, "effects", "rotate_step", "Rotate step (degrees)", SettingKind.Real)
            {
                Default = "15", Min = 0.5, Max = 180, Step = 0.5, Group = "Rotation",
            },
            new(Effects, "effects", "rotate_release", "Twist release", SettingKind.Choice)
            {
                Default = "\"exact\"", Choices = ["exact", "spin"], Group = "Rotation",
                Note = "Exact keeps the angle the fingers left. Spin carries their speed; stop-spin or a grab catches it.",
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
            new(Canvas, "canvas", "grid_animation", "Grid reveal", SettingKind.Choice) { Default = "\"fade\"", Choices = GridAnimations },
            new(Canvas, "canvas", "grid_animation_ms", "Grid reveal (ms)", SettingKind.Integer) { Default = "200", Min = 0, Max = 5000 },
            new(Canvas, "canvas", "grid_easing", "Grid reveal easing", SettingKind.Choice) { Default = "\"out-cubic\"", Choices = GridEasings.Names },
            new(Canvas, "canvas", "grid_linger_ms", "Grid linger (ms)", SettingKind.Integer) { Default = "150", Min = 0, Max = 5000 },
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
            .. GridEffectRows(Canvas, "canvas.grid_effects"),

            new(Touch, "touch", "hide_cursor", "Hide cursor on touch", SettingKind.Flag) { Default = "true" },
            new(Touch, "touch", "titlebar_twist", "Twist with a finger on the titlebar", SettingKind.Flag) { Default = "true" },
            new(Touch, "touch", "window_twist", "Fingers that twist a window anywhere", SettingKind.Integer)
            {
                Default = "3", Min = 0, Max = 5, Note = "0 turns it off; 1 and 2 are refused, since apps own them.",
            },
            new(Touch, "touch", "hold_move", "Hold two fingers, then slide, to move", SettingKind.Flag) { Default = "true" },
            new(Touch, "touch", "hold_ms", "Hold before a move, ms", SettingKind.Integer) { Default = "300", Min = 50, Max = 2000 },
            new(Touch, "touch", "hold_move_turn", "Turn while moving", SettingKind.Flag) { Default = "true" },
            new(Physics, "physics", "enable", "Physics", SettingKind.Flag) { Default = "false" },
            new(Physics, "physics", "engine", "Engine", SettingKind.Choice) { Default = "\"bepu\"", Choices = PhysicsSetting.EngineNames },
            new(Physics, "physics", "gravity", "Gravity", SettingKind.Real) { Default = "0", Min = -10000, Max = 10000, Step = 100 },
            new(Physics, "physics", "gravity_steps", "Gravity steps", SettingKind.Reals)
            {
                Default = "[0, 2000]", Min = -10000, Max = 10000, Note = "The gravity action cycles these.",
            },
            new(Physics, "physics", "friction", "Friction", SettingKind.Real) { Default = "0.3", Min = 0, Max = 2, Step = 0.05 },
            new(Physics, "physics", "restitution", "Restitution", SettingKind.Real) { Default = "0.2", Min = 0, Max = 1, Step = 0.05 },
            new(Physics, "physics", "density", "Density", SettingKind.Real) { Default = "1.0", Min = 0.1, Max = 10, Step = 0.1 },
            new(Physics, "physics", "damping", "Damping", SettingKind.Real) { Default = "2.0", Min = 0, Max = 20, Step = 0.1 },
            new(Physics, "physics", "throw_multiplier", "Throw multiplier", SettingKind.Real) { Default = "1.0", Min = 0, Max = 5, Step = 0.1 },
            new(Physics, "physics", "max_throw_speed", "Max throw speed", SettingKind.Real) { Default = "6000", Min = 0, Max = 20000, Step = 100 },
            new(Physics, "physics", "stop_speed", "Stop speed", SettingKind.Real) { Default = "20", Min = 0, Max = 200, Step = 1 },
            new(Physics, "physics", "impact_speed", "Impact speed", SettingKind.Real) { Default = "600", Min = 0, Max = 5000, Step = 50 },
            new(Physics, "physics", "rotation", "Rotation", SettingKind.Flag) { Default = "true" },
            new(Physics, "physics", "upright", "Upright at rest", SettingKind.Flag) { Default = "true" },
            new(Physics, "physics", "impacts", "Impacts", SettingKind.Names) { Default = "[\"squash\"]", Choices = ["squash", "shake"] },

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
            new(Overview, "overview", "wall", "Wall", SettingKind.Choice) { Default = "\"step\"", Choices = ["slope", "step"] },
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
            new(Overview, "overview", "grid_animation", "Grid reveal", SettingKind.Choice) { Default = "\"fade\"", Choices = GridAnimations },
            new(Overview, "overview", "grid_animation_ms", "Grid reveal (ms)", SettingKind.Integer) { Default = "200", Min = 0, Max = 5000 },
            new(Overview, "overview", "grid_easing", "Grid reveal easing", SettingKind.Choice) { Default = "\"out-cubic\"", Choices = GridEasings.Names },
            new(Overview, "overview", "grid_linger_ms", "Grid linger (ms)", SettingKind.Integer) { Default = "150", Min = 0, Max = 5000 },
            new(Overview, "overview", "desktop_grid", "Grid on the desktop", SettingKind.Flag) { Default = "false" },
            new(Overview, "overview", "animation_ms", "Animation (ms)", SettingKind.Integer) { Default = "250", Min = 0, Max = 5000 },
            .. GridEffectRows(Overview, "overview.grid_effects"),

            new(Hypr, "hypr", "enable", "hyprland-protocols", SettingKind.Flag) { Default = "true", Restart = true },
            new(Hypr, "hypr", "input_capture", "Input capture", SettingKind.Flag) { Default = "true", Restart = true },
            new(Hypr, "hypr", "ctm", "CTM control", SettingKind.Flag) { Default = "true", Restart = true },

            new(Panel, "settings", "palette", "Palette", SettingKind.Choice) { Default = "\"dark\"", Choices = ["dark", "light"] },
            new(Panel, "settings", "font_size", "Font size", SettingKind.Real) { Default = "14", Min = 10, Max = 24, Step = 1 },
        ];
    }

    private static SettingKey[] GridEffectRows(string section, string table) =>
    [
        new(section, table, "glow", "Glow", SettingKind.Real) { Default = "0.0", Min = 0, Max = 1, Step = 0.05, Group = GridEffectsGroup },
        new(section, table, "glow_radius", "Glow radius", SettingKind.Real)
        {
            Default = "6.0", Min = 1, Max = 64, Step = 1, Group = GridEffectsGroup,
        },
        new(section, table, "glow_path", "Glow path", SettingKind.Choice)
        {
            Default = "\"auto\"", Choices = ["auto", "shader", "halo", "none"], Group = GridEffectsGroup,
            Note = "Auto draws the shader glow where the renderer compiles it and the halo elsewhere.",
        },
        new(section, table, "highlight_color", "Highlight color", SettingKind.Color) { Default = "\"#e8ecff\"", Group = GridEffectsGroup },
        new(section, table, "highlight_radius", "Highlight radius", SettingKind.Real)
        {
            Default = "160.0", Min = 0, Max = 4096, Step = 10, Group = GridEffectsGroup,
        },
        new(section, table, "highlight_dragged", "Moved window", SettingKind.Real)
        {
            Default = "0.0", Min = 0, Max = 1, Step = 0.05, Group = GridEffectsGroup,
        },
        new(section, table, "highlight_focused", "Focused window", SettingKind.Real)
        {
            Default = "0.0", Min = 0, Max = 1, Step = 0.05, Group = GridEffectsGroup,
        },
        new(section, table, "highlight_windows", "Other windows", SettingKind.Real)
        {
            Default = "0.0", Min = 0, Max = 1, Step = 0.05, Group = GridEffectsGroup,
        },
        new(section, table, "highlight_pointer", "Pointer", SettingKind.Real)
        {
            Default = "0.0", Min = 0, Max = 1, Step = 0.05, Group = GridEffectsGroup,
        },
        new(section, table, "highlight_glow", "Glow near a source", SettingKind.Real)
        {
            Default = "2.0", Min = 0, Max = 8, Step = 0.25, Group = GridEffectsGroup,
        },
        new(section, table, "highlight_spring", "Highlight spring", SettingKind.Real)
        {
            Default = "300.0", Min = 1, Max = 5000, Step = 10, Group = GridEffectsGroup,
        },
        new(section, table, "highlight_damping", "Highlight damping", SettingKind.Real)
        {
            Default = "1.0", Min = 0.1, Max = 4, Step = 0.05, Group = GridEffectsGroup,
        },
        new(section, table, "pinch", "Pinch", SettingKind.Real)
        {
            Default = "0.0", Min = -0.9, Max = 4, Step = 0.05, Group = GridEffectsGroup,
            Note = "Extra fan toward a moved or fast window. The step overview takes 0 to 0.7.",
        },
        new(section, table, "pinch_reach", "Pinch reach", SettingKind.Real)
        {
            Default = "400.0", Min = 0, Max = 4096, Step = 10, Group = GridEffectsGroup,
        },
        new(section, table, "pinch_speed", "Pinch speed", SettingKind.Real)
        {
            Default = "600.0", Min = 0, Max = 20000, Step = 50, Group = GridEffectsGroup,
        },
        new(section, table, "pinch_spring", "Pinch spring", SettingKind.Real)
        {
            Default = "200.0", Min = 1, Max = 5000, Step = 10, Group = GridEffectsGroup,
        },
        new(section, table, "pinch_damping", "Pinch damping", SettingKind.Real)
        {
            Default = "1.1", Min = 0.1, Max = 4, Step = 0.05, Group = GridEffectsGroup,
        },
        new(section, table, "density", "Dim packed lines", SettingKind.Flag) { Default = "false", Group = GridEffectsGroup },
        new(section, table, "density_spacing", "Density spacing", SettingKind.Real)
        {
            Default = "12.0", Min = 1, Max = 256, Step = 1, Group = GridEffectsGroup,
        },
        new(section, table, "density_floor", "Density floor", SettingKind.Real)
        {
            Default = "0.15", Min = 0, Max = 1, Step = 0.05, Group = GridEffectsGroup,
        },
    ];

    private const string GridEffectsGroup = "Grid effects";

    private static SettingKey Flag(string section, string table, string key, string label, string group) =>
        new(section, table, key, label, SettingKind.Flag) { Default = "false", Group = group };
}
