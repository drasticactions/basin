using System.Globalization;

using Basin.Diagnostics;

namespace Westonia;

public sealed class WestonIni
{
    private static readonly string[] RefusedSections = ["rdp", "vnc", "pipewire"];

    public WestonCoreSection Core { get; } = new();

    public WestonShellSection Shell { get; } = new();

    public WestonScreensaverSection Screensaver { get; } = new();

    public WestonKeyboardSection Keyboard { get; } = new();

    public WestonAutolaunchSection Autolaunch { get; } = new();

    public List<WestonLauncher> Launchers { get; } = [];

    public List<WestonOutputSection> Outputs { get; } = [];

    public Dictionary<string, string> Libinput { get; } = [];

    public string? XWaylandPath { get; set; }

    public string? InputMethodPath { get; set; }

    public string? InputMethodArgs { get; set; }

    public string? Path { get; private set; }

    public List<string> Refusals { get; } = [];

    public static string? Locate(string? explicitPath)
    {
        if (explicitPath is not null)
        {
            return explicitPath;
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg))
        {
            var candidate = System.IO.Path.Combine(xdg, "weston.ini");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            var candidate = System.IO.Path.Combine(home, ".config", "weston.ini");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static WestonIni Load(string? path, BasinLogger log = default)
    {
        var ini = new WestonIni();
        var resolved = Locate(path);
        if (resolved is null || !File.Exists(resolved))
        {
            return ini;
        }

        ini.Path = resolved;
        ini.ParseLines(File.ReadAllLines(resolved));
        foreach (var refusal in ini.Refusals)
        {
            log.Warn($"{refusal}");
        }

        return ini;
    }

    public static WestonIni FromLines(IEnumerable<string> lines)
    {
        var ini = new WestonIni();
        ini.ParseLines(lines);
        return ini;
    }

    private void ParseLines(IEnumerable<string> lines)
    {
        var section = string.Empty;
        WestonLauncherBuilder? launcher = null;
        WestonOutputSection? output = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                Flush(ref launcher, ref output);
                section = line[1..^1].Trim().ToLowerInvariant();
                if (section == "launcher")
                {
                    launcher = new WestonLauncherBuilder();
                }
                else if (section == "output")
                {
                    output = new WestonOutputSection();
                }
                else if (RefusedSections.Contains(section))
                {
                    Refusals.Add($"weston.ini: [{section}] is refused: basin has no remote backend");
                }

                continue;
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var key = line[..split].Trim().ToLowerInvariant();
            var value = line[(split + 1)..].Trim();
            Apply(section, key, value, launcher, output);
        }

        Flush(ref launcher, ref output);
    }

    private void Flush(ref WestonLauncherBuilder? launcher, ref WestonOutputSection? output)
    {
        if (launcher is { Path: not null })
        {
            Launchers.Add(new WestonLauncher(launcher.Icon ?? string.Empty, launcher.Path, launcher.DisplayName));
        }

        if (output is { Name: not null })
        {
            Outputs.Add(output);
        }

        launcher = null;
        output = null;
    }

    private void Apply(
        string section,
        string key,
        string value,
        WestonLauncherBuilder? launcher,
        WestonOutputSection? output)
    {
        switch (section)
        {
            case "core":
                ApplyCore(key, value);
                return;
            case "shell":
                ApplyShell(key, value);
                return;
            case "launcher":
                if (launcher is null)
                {
                    return;
                }

                switch (key)
                {
                    case "icon":
                        launcher.Icon = value;
                        return;
                    case "path":
                        launcher.Path = value;
                        return;
                    case "displayname":
                        launcher.DisplayName = value;
                        return;
                    default:
                        Refuse(section, key);
                        return;
                }

            case "screensaver":
                switch (key)
                {
                    case "path":
                        Screensaver.Path = value;
                        return;
                    case "duration":
                        Screensaver.DurationSeconds = Int(value, Screensaver.DurationSeconds);
                        return;
                    default:
                        Refuse(section, key);
                        return;
                }

            case "output":
                ApplyOutput(key, value, output);
                return;
            case "keyboard":
                ApplyKeyboard(key, value);
                return;
            case "libinput":
                Libinput[key] = value;
                return;
            case "touchpad":
                Refusals.Add(
                    $"weston.ini: [touchpad] {key} is obsolete and ignored: libinput has no equivalent");
                return;
            case "xwayland":
                if (key == "path")
                {
                    XWaylandPath = value;
                    return;
                }

                Refuse(section, key);
                return;
            case "input-method":
                switch (key)
                {
                    case "path":
                        InputMethodPath = value;
                        return;
                    case "args":
                        InputMethodArgs = value;
                        return;
                    default:
                        Refuse(section, key);
                        return;
                }

            case "autolaunch":
                switch (key)
                {
                    case "path":
                        Autolaunch.Path = value;
                        return;
                    case "watch":
                        Autolaunch.Watch = Bool(value, Autolaunch.Watch);
                        return;
                    default:
                        Refuse(section, key);
                        return;
                }

            default:
                if (RefusedSections.Contains(section))
                {
                    return;
                }

                Refuse(section, key);
                return;
        }
    }

    private void ApplyCore(string key, string value)
    {
        switch (key)
        {
            case "xwayland":
                Core.XWayland = Bool(value, Core.XWayland);
                return;
            case "shell":
                Core.Shell = value;
                return;
            case "gbm-format":
                Core.GbmFormat = value;
                return;
            case "require-input":
                Core.RequireInput = Bool(value, Core.RequireInput);
                return;
            case "idle-time":
                Core.IdleTimeSeconds = Int(value, Core.IdleTimeSeconds);
                return;
            case "repaint-window":
                Core.RepaintWindowMillis = Int(value, Core.RepaintWindowMillis);
                return;
            case "renderer":
                Core.Renderer = value;
                return;
            case "use-gl":
                if (Bool(value, false))
                {
                    Core.Renderer = "gl";
                }

                return;
            case "use-pixman":
                if (Bool(value, false))
                {
                    Core.Renderer = "pixman";
                }

                return;
            case "use-vulkan":
                if (Bool(value, false))
                {
                    Core.Renderer = "vulkan";
                }

                return;
            case "modules":
                Refusals.Add("weston.ini: [core] modules is refused: basin has no plugin loader");
                return;
            default:
                Refuse("core", key);
                return;
        }
    }

    private void ApplyShell(string key, string value)
    {
        switch (key)
        {
            case "background-image":
                Shell.BackgroundImage = value;
                return;
            case "background-color":
                Shell.BackgroundColor = Color(value, Shell.BackgroundColor);
                return;
            case "background-type":
                Shell.BackgroundType = value switch
                {
                    "scale" => BackgroundType.Scale,
                    "scale-crop" => BackgroundType.ScaleCrop,
                    "scale-fit" => BackgroundType.ScaleFit,
                    "centered" => BackgroundType.Centered,
                    _ => BackgroundType.Tile,
                };
                return;
            case "panel-color":
                Shell.PanelColor = Color(value, Shell.PanelColor);
                return;
            case "panel-position":
                Shell.PanelPosition = value switch
                {
                    "bottom" => PanelPosition.Bottom,
                    "left" => PanelPosition.Left,
                    "right" => PanelPosition.Right,
                    "none" => PanelPosition.None,
                    _ => PanelPosition.Top,
                };
                return;
            case "locking":
                Shell.Locking = Bool(value, Shell.Locking);
                return;
            case "animation":
                Shell.Animation = Animation(value, Shell.Animation);
                return;
            case "startup-animation":
                Shell.StartupAnimation = Animation(value, Shell.StartupAnimation);
                return;
            case "close-animation":
                Shell.CloseAnimation = Animation(value, Shell.CloseAnimation);
                return;
            case "focus-animation":
                Shell.FocusAnimation = Animation(value, Shell.FocusAnimation);
                return;
            case "client":
                Shell.Client = value;
                return;
            case "binding-modifier":
                Shell.BindingModifier = value.ToLowerInvariant();
                return;
            case "num-workspaces":
                Shell.NumWorkspaces = Math.Clamp(Int(value, Shell.NumWorkspaces), 1, 32);
                return;
            case "cursor-theme":
                Shell.CursorTheme = value;
                return;
            case "cursor-size":
                Shell.CursorSize = Int(value, Shell.CursorSize);
                return;
            case "allow-zap":
                Shell.AllowZap = Bool(value, Shell.AllowZap);
                return;
            case "clock-format":
                Shell.ClockFormat = value switch
                {
                    "seconds" => ClockFormat.Seconds,
                    "minutes-24h" => ClockFormat.Minutes24H,
                    "seconds-24h" => ClockFormat.Seconds24H,
                    "none" => ClockFormat.None,
                    _ => ClockFormat.Minutes,
                };
                return;
            case "disallow-output-changed-move":
                Shell.DisallowOutputChangedMove = Bool(value, Shell.DisallowOutputChangedMove);
                return;
            default:
                Refuse("shell", key);
                return;
        }
    }

    private void ApplyOutput(string key, string value, WestonOutputSection? output)
    {
        if (output is null)
        {
            return;
        }

        switch (key)
        {
            case "name":
                output.Name = value;
                return;
            case "mode":
                output.Mode = value;
                return;
            case "scale":
                output.Scale = Double(value, output.Scale);
                return;
            case "transform":
                output.Transform = value;
                return;
            case "icc_profile":
                output.IccProfile = value;
                return;
            case "vrr-mode":
                output.VrrMode = value;
                return;
            case "max-bpc":
                output.MaxBpc = Int(value, 0);
                return;
            case "eotf-mode":
                output.EotfMode = value;
                return;
            case "colorimetry-mode":
                output.ColorimetryMode = value;
                return;
            case "mirror-of":
            case "clone-of":
                Refusals.Add($"weston.ini: [output] {key} is refused: basin has no output mirroring");
                return;
            default:
                Refuse("output", key);
                return;
        }
    }

    private void ApplyKeyboard(string key, string value)
    {
        switch (key)
        {
            case "keymap_rules":
                Keyboard.Rules = value;
                return;
            case "keymap_model":
                Keyboard.Model = value;
                return;
            case "keymap_layout":
                Keyboard.Layout = value;
                return;
            case "keymap_variant":
                Keyboard.Variant = value;
                return;
            case "keymap_options":
                Keyboard.Options = value;
                return;
            case "repeat-rate":
                Keyboard.RepeatRate = Int(value, Keyboard.RepeatRate);
                return;
            case "repeat-delay":
                Keyboard.RepeatDelay = Int(value, Keyboard.RepeatDelay);
                return;
            case "numlock-on":
                Keyboard.NumlockOn = Bool(value, Keyboard.NumlockOn);
                return;
            case "vt-switching":
                Keyboard.VtSwitching = Bool(value, Keyboard.VtSwitching);
                return;
            default:
                Refuse("keyboard", key);
                return;
        }
    }

    private void Refuse(string section, string key) =>
        Refusals.Add($"weston.ini: [{section}] {key} is not honoured");

    private static ShellAnimation Animation(string value, ShellAnimation fallback) => value switch
    {
        "zoom" => ShellAnimation.Zoom,
        "fade" => ShellAnimation.Fade,
        "dim-layer" => ShellAnimation.DimLayer,
        "none" => ShellAnimation.None,
        _ => fallback,
    };

    private static bool Bool(string value, bool fallback) => value.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" => false,
        _ => fallback,
    };

    private static int Int(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static double Double(string value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static uint Color(string value, uint fallback)
    {
        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }
        else if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            return fallback;
        }

        return text.Length <= 6 ? 0xFF000000u | parsed : parsed;
    }

    private sealed class WestonLauncherBuilder
    {
        public string? Icon { get; set; }

        public string? Path { get; set; }

        public string? DisplayName { get; set; }
    }
}
