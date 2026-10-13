using Basin.Config;
using Basin.Diagnostics;
using Basin.Greetd;

namespace BasinGreeter;

internal sealed class GreeterConfig
{
    public const string DefaultPath = "/etc/basin-greeter/basin-greeter.toml";

    public string Theme { get; private set; } = "auto";

    public string Background { get; private set; } = "";

    public uint BackgroundColor { get; private set; } = 0xff203040;

    public uint Accent { get; private set; } = 0xff3b82f6;

    public string Font { get; private set; } = "";

    public string Clock { get; private set; } = "%H:%M";

    public GreeterSettings Settings { get; private set; } = new();

    public string Renderer { get; private set; } = "";

    public string? LoadedFrom { get; private set; }

    public bool Failed { get; private set; }

    public IEnumerable<string> RestartKeysChanged(GreeterConfig next)
    {
        if (Renderer != next.Renderer)
        {
            yield return "render.renderer";
        }
    }

    public static GreeterConfig Load(string? path, BasinLogger log)
    {
        var config = new GreeterConfig();
        if (path is null)
        {
            return config;
        }

        if (!File.Exists(path))
        {
            log.Info($"{path} does not exist; every key is at its default");
            return config;
        }

        if (TomlConfig.Read(path, out var failure) is not { } table)
        {
            log.Warn($"{path}: {failure}; every key is at its default");
            config.Failed = true;
            return config;
        }

        config.LoadedFrom = path;
        config.Read(new TomlReader(table, log));
        return config;
    }

    public static GreeterConfig Parse(string text, BasinLogger log)
    {
        var config = new GreeterConfig();
        config.Read(new TomlReader(Tomlyn.Toml.ToModel(text), log));
        return config;
    }

    private void Read(TomlReader root)
    {
        if (root.Section("look") is { } look)
        {
            Theme = look.Choice("theme", Theme, "auto", "light", "dark");
            Background = look.Text("background") ?? Background;
            BackgroundColor = TomlColor.Argb(look.Text("background_color"), BackgroundColor);
            Accent = TomlColor.Argb(look.Text("accent"), Accent);
            Font = look.Text("font") ?? Font;
            Clock = look.Text("clock") ?? Clock;
        }

        Settings = GreeterSettings.Read(root);

        if (root.Section("render") is { } render)
        {
            Renderer = render.Text("renderer") ?? Renderer;
        }

        root.ReportUnknown();
    }
}
