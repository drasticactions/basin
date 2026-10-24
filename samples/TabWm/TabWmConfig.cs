using AvaUnion.Engine;
using Basin.Config;
using Basin.Diagnostics;

namespace TabWm;

internal sealed class TabWmConfig
{
    public string? Path { get; private set; }

    public UnionEngineKind Engine { get; set; } = UnionEngineKind.Auto;

    public ThemeVariantChoice Variant { get; set; } = ThemeVariantChoice.Auto;

    public string? ColorScheme { get; set; }

    public bool XWayland { get; set; } = true;

    public bool CloseWhenEmpty { get; set; }

    public int CloseTimeoutMs { get; set; } = 5000;

    public IReadOnlyList<string> Pinned { get; private set; } = ["org.kde.konsole.desktop", "foot.desktop", "firefox.desktop"];

    public string? Terminal { get; set; }

    public bool Restore { get; set; } = true;

    public string? LockCommand { get; set; } = "swaylock -f";

    public IReadOnlyList<TabBinding> Bindings { get; private set; } = [];

    public List<string> Startup { get; } = [];

    public static Stream TemplateStream() =>
        typeof(TabWmConfig).Assembly.GetManifestResourceStream("tab-wm.toml")
            ?? throw new InvalidOperationException("The tab-wm.toml template is not embedded in the program.");

    public static TabWmConfig Load(string? path, bool none, BasinLogger log, out string? fatal)
    {
        var config = new TabWmConfig();
        var load = TomlConfig.Load(path, "tab-wm", none, TemplateStream, log);
        fatal = load.Fatal;
        config.Path = load.File;
        if (load.Table is not { } table)
        {
            config.Bindings = TabBindings.Build(null, log);
            return config;
        }

        config.Read(new TomlReader(table, log), log);
        return config;
    }

    public static TabWmConfig FromText(string text, BasinLogger log)
    {
        var config = new TabWmConfig();
        config.Read(new TomlReader(Tomlyn.Toml.ToModel(text), log), log);
        return config;
    }

    private void Read(TomlReader reader, BasinLogger log)
    {
        if (reader.Section("theme") is { } theme)
        {
            Engine = theme.Choice("engine", "auto", "auto", "native", "managed") switch
            {
                "native" => UnionEngineKind.Native,
                "managed" => UnionEngineKind.Managed,
                _ => UnionEngineKind.Auto,
            };
            Variant = theme.Choice("variant", "auto", "auto", "light", "dark") switch
            {
                "light" => ThemeVariantChoice.Light,
                "dark" => ThemeVariantChoice.Dark,
                _ => ThemeVariantChoice.Auto,
            };
            ColorScheme = theme.Text("color-scheme") is { Length: > 0 } scheme ? scheme : null;
            theme.ReportUnknown();
        }

        if (reader.Section("shell") is { } shell)
        {
            XWayland = shell.Flag("xwayland", XWayland);
            shell.ReportUnknown();
        }

        if (reader.Section("tabs") is { } tabs)
        {
            CloseWhenEmpty = tabs.Flag("close-when-empty", CloseWhenEmpty);
            CloseTimeoutMs = Math.Max(0, tabs.Number("close-timeout-ms", CloseTimeoutMs));
            tabs.ReportUnknown();
        }

        if (reader.Section("launcher") is { } launcher)
        {
            if (launcher.Words("pinned") is { } pinned)
            {
                Pinned = pinned;
            }

            Terminal = launcher.Text("terminal") is { Length: > 0 } terminal ? terminal : null;
            launcher.ReportUnknown();
        }

        if (reader.Section("session") is { } session)
        {
            Restore = session.Flag("restore", Restore);
            LockCommand = session.Text("lock-command") is { } lockCommand ? lockCommand : LockCommand;
            session.ReportUnknown();
        }

        Bindings = TabBindings.Build(reader.Free("keys"), log);
        foreach (var startup in reader.Sections("startup"))
        {
            if (startup.Text("command") is { Length: > 0 } command)
            {
                Startup.Add(command);
            }

            startup.ReportUnknown();
        }

        reader.ReportUnknown();
    }
}
