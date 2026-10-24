using Basin.Config;
using Basin.Diagnostics;
using Basin.Greetd;

namespace EightWm;

internal sealed class EightGreeterConfig
{
    public const string DefaultPath = "/etc/eight-wm/greeter.toml";

    public GreeterSettings Settings { get; private set; } = new();

    public bool Dark { get; private set; } = true;

    public uint Accent { get; private set; } = Shell.DefaultAccent;

    public string Picture { get; private set; } = "";

    public bool Clock24 { get; private set; }

    public string Renderer { get; private set; } = "";

    public bool Failed { get; private set; }

    public IEnumerable<string> RestartKeysChanged(EightGreeterConfig next)
    {
        if (Renderer != next.Renderer)
        {
            yield return "render.renderer";
        }

        if (Dark != next.Dark)
        {
            yield return "look.theme";
        }
    }

    public static EightGreeterConfig Load(string? path, BasinLogger log)
    {
        var config = new EightGreeterConfig();
        if (path is null)
        {
            return config;
        }

        if (!File.Exists(path))
        {
            log.Info($"config: {path} does not exist, every key is at its default");
            return config;
        }

        if (TomlConfig.Read(path, out var failure) is not { } table)
        {
            log.Warn($"config: {path} not read, {failure}, defaults used");
            config.Failed = true;
            return config;
        }

        config.Read(new TomlReader(table, log));
        return config;
    }

    public static EightGreeterConfig Parse(string text, BasinLogger log)
    {
        var config = new EightGreeterConfig();
        config.Read(new TomlReader(Tomlyn.Toml.ToModel(text), log));
        return config;
    }

    private void Read(TomlReader root)
    {
        Settings = GreeterSettings.Read(root);
        if (root.Section("look") is { } look)
        {
            Dark = look.Choice("theme", "dark", "dark", "light") != "light";
            Accent = TomlColor.Argb(look.Text("accent"), Accent) | 0xff000000;
            Picture = look.Text("picture") ?? Picture;
            Clock24 = look.Choice("clock", "12", "12", "24") == "24";
        }

        if (root.Section("render") is { } render)
        {
            Renderer = render.Text("renderer") ?? Renderer;
        }

        root.ReportUnknown();
    }
}
