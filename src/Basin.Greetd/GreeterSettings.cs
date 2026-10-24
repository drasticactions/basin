using Basin.Config;
using Tomlyn.Model;

namespace Basin.Greetd;

/// <summary>
/// The settings that every greeter shares, read from a TOML file. A program reads its own keys from the same reader.
/// </summary>
public sealed class GreeterSettings
{
    /// <summary>
    /// Gets a value indicating whether the greeter lists the users. When it is false, the greeter asks for a name. The key is <c>users.list</c>.
    /// </summary>
    public bool ListUsers { get; private set; } = true;

    /// <summary>
    /// Gets the login names that the user list leaves out. The key is <c>users.hide</c>.
    /// </summary>
    public IReadOnlySet<string> HideUsers { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the lowest UID to list. Zero uses <c>UID_MIN</c> from login.defs. The key is <c>users.min_uid</c>.
    /// </summary>
    public uint MinUid { get; private set; }

    /// <summary>
    /// Gets the highest UID to list. Zero uses <c>UID_MAX</c> from login.defs. The key is <c>users.max_uid</c>.
    /// </summary>
    public uint MaxUid { get; private set; }

    /// <summary>
    /// Gets the id of the session to select when the user has no remembered session. Empty selects the first Wayland session. The key is <c>sessions.default</c>.
    /// </summary>
    public string DefaultSession { get; private set; } = "";

    /// <summary>
    /// Gets the arguments that go before an X11 session command. Empty offers no X11 session. The key is <c>sessions.x11_wrapper</c>.
    /// </summary>
    public IReadOnlyList<string> X11Wrapper { get; private set; } = ["startx"];

    /// <summary>
    /// Gets the path of the environments file. The key is <c>sessions.environments</c>.
    /// </summary>
    public string Environments { get; private set; } = "/etc/greetd/environments";

    /// <summary>
    /// Gets a value indicating whether a typed command can replace the selected session. The key is <c>sessions.allow_command</c>.
    /// </summary>
    public bool AllowCommand { get; private set; }

    /// <summary>
    /// Gets the keyboard layout ids that the greeter offers, for example <c>us(intl)</c>. Empty keeps the system keymap. The key is <c>keyboard.layouts</c>.
    /// </summary>
    public IReadOnlyList<string> Layouts { get; private set; } = [];

    /// <summary>
    /// Gets when the on-screen keyboard shows: <c>auto</c>, <c>always</c> or <c>never</c>. The key is <c>keyboard.on_screen</c>.
    /// </summary>
    public string OnScreen { get; private set; } = "auto";

    /// <summary>
    /// Gets the size of the on-screen keyboard: <c>xs</c>, <c>s</c>, <c>m</c> or <c>l</c>. The key is <c>keyboard.on_screen_size</c>.
    /// </summary>
    public string OnScreenSize { get; private set; } = "m";

    /// <summary>
    /// Gets a value indicating whether the greeter offers the power actions. The key is <c>power.enabled</c>.
    /// </summary>
    public bool Power { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the greeter shows the battery status. The key is <c>status.battery</c>.
    /// </summary>
    public bool Battery { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the greeter shows the network status. The key is <c>status.network</c>.
    /// </summary>
    public bool Network { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the greeter lets the person join a network. The key is <c>network.join</c>.
    /// </summary>
    public bool NetworkJoin { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the greeter starts with high contrast. The key is <c>accessibility.high_contrast</c>.
    /// </summary>
    public bool HighContrast { get; private set; }

    /// <summary>
    /// Gets the zoom of the login box, clamped to the range 1.0 to 2.0. The key is <c>accessibility.text_scale</c>.
    /// </summary>
    public double TextScale { get; private set; } = 1.0;

    /// <summary>
    /// Gets the screen reader mode: <c>off</c>, <c>toggle</c> or <c>on</c>. The key is <c>accessibility.screen_reader</c>.
    /// </summary>
    public string ScreenReader { get; private set; } = "toggle";

    /// <summary>
    /// Gets the command line that starts the screen reader. The key is <c>accessibility.orca</c>.
    /// </summary>
    public IReadOnlyList<string> Orca { get; private set; } = ["orca", "--replace"];

    /// <summary>
    /// Gets the keyboard aids of the <c>[accessibility.keyboard]</c> table: <c>sticky</c>, <c>sticky_shortcut</c>, <c>slow_ms</c>, <c>bounce_ms</c>, <c>mouse_keys</c> and <c>mouse_keys_speed</c>. Every aid is off by default.
    /// </summary>
    public Basin.Capabilities.KeyboardAidOptions KeyboardAids { get; private set; } = new();

    /// <summary>
    /// Gets the settings of the <c>[handoff]</c> table, which control what the greeter leaves on screen for the session.
    /// </summary>
    public HandoffSettings Handoff { get; private set; } = new();

    /// <summary>
    /// Reads the shared tables from <paramref name="root"/>. A missing table or key keeps its default. A string where a list is expected counts as a list of one.
    /// </summary>
    /// <param name="root">The reader for the whole file. Only the keys that the settings use are marked as consumed.</param>
    /// <returns>New settings.</returns>
    public static GreeterSettings Read(TomlReader root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var settings = new GreeterSettings();
        if (root.Section("users") is { } users)
        {
            settings.ListUsers = users.Flag("list", settings.ListUsers);
            settings.HideUsers = new HashSet<string>(List(users, "hide") ?? [], StringComparer.Ordinal);
            settings.MinUid = (uint)Math.Max(0, users.Number("min_uid", 0));
            settings.MaxUid = (uint)Math.Max(0, users.Number("max_uid", 0));
        }

        if (root.Section("sessions") is { } sessions)
        {
            settings.DefaultSession = sessions.Text("default") ?? settings.DefaultSession;
            settings.X11Wrapper = List(sessions, "x11_wrapper") ?? settings.X11Wrapper;
            settings.Environments = sessions.Text("environments") ?? settings.Environments;
            settings.AllowCommand = sessions.Flag("allow_command", settings.AllowCommand);
        }

        if (root.Section("keyboard") is { } keyboard)
        {
            settings.Layouts = List(keyboard, "layouts") ?? settings.Layouts;
            settings.OnScreen = keyboard.Choice("on_screen", settings.OnScreen, "auto", "always", "never");
            settings.OnScreenSize = keyboard.Choice("on_screen_size", settings.OnScreenSize, "xs", "s", "m", "l");
        }

        if (root.Section("power") is { } power)
        {
            settings.Power = power.Flag("enabled", settings.Power);
        }

        if (root.Section("status") is { } status)
        {
            settings.Battery = status.Flag("battery", settings.Battery);
            settings.Network = status.Flag("network", settings.Network);
        }

        if (root.Section("network") is { } network)
        {
            settings.NetworkJoin = network.Flag("join", settings.NetworkJoin);
        }

        if (root.Section("accessibility") is { } accessibility)
        {
            settings.HighContrast = accessibility.Flag("high_contrast", settings.HighContrast);
            settings.TextScale = Math.Clamp(accessibility.Number("text_scale", settings.TextScale), 1.0, 2.0);
            settings.ScreenReader = accessibility.Choice("screen_reader", settings.ScreenReader, "off", "toggle", "on");
            settings.Orca = List(accessibility, "orca") ?? settings.Orca;
            if (accessibility.Section("keyboard") is { } keys)
            {
                var aids = settings.KeyboardAids;
                settings.KeyboardAids = aids with
                {
                    Sticky = keys.Flag("sticky", aids.Sticky),
                    StickyShortcut = keys.Flag("sticky_shortcut", aids.StickyShortcut),
                    SlowMs = Math.Max(0, keys.Number("slow_ms", aids.SlowMs)),
                    BounceMs = Math.Max(0, keys.Number("bounce_ms", aids.BounceMs)),
                    MouseKeys = keys.Flag("mouse_keys", aids.MouseKeys),
                    MouseKeysMaxSpeed = Math.Max(1.0, keys.Number("mouse_keys_speed", aids.MouseKeysMaxSpeed)),
                };
            }
        }

        settings.Handoff = HandoffSettings.Read(root.Section("handoff"));
        return settings;
    }

    private static string[]? List(TomlReader reader, string key) => reader.Raw(key) switch
    {
        TomlArray array => [.. array.OfType<string>()],
        string single when single.Length > 0 => [single],
        string => [],
        _ => null,
    };
}
