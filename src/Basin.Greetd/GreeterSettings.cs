using Basin.Config;
using Tomlyn.Model;

namespace Basin.Greetd;

public sealed class GreeterSettings
{
    public bool ListUsers { get; private set; } = true;

    public IReadOnlySet<string> HideUsers { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    public uint MinUid { get; private set; }

    public uint MaxUid { get; private set; }

    public string DefaultSession { get; private set; } = "";

    public IReadOnlyList<string> X11Wrapper { get; private set; } = ["startx"];

    public string Environments { get; private set; } = "/etc/greetd/environments";

    public bool AllowCommand { get; private set; }

    public IReadOnlyList<string> Layouts { get; private set; } = [];

    public string OnScreen { get; private set; } = "auto";

    public string OnScreenSize { get; private set; } = "m";

    public bool Power { get; private set; } = true;

    public bool Battery { get; private set; } = true;

    public bool Network { get; private set; } = true;

    public bool NetworkJoin { get; private set; } = true;

    public bool HighContrast { get; private set; }

    public double TextScale { get; private set; } = 1.0;

    public string ScreenReader { get; private set; } = "toggle";

    public IReadOnlyList<string> Orca { get; private set; } = ["orca", "--replace"];

    public HandoffSettings Handoff { get; private set; } = new();

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
