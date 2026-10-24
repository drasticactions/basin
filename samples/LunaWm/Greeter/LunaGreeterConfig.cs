using Basin.Config;
using Basin.Diagnostics;
using Basin.Greetd;

namespace LunaWm;

internal sealed class LunaGreeterConfig
{
    public const string DefaultPath = "/etc/luna-wm/greeter.toml";

    public GreeterSettings Settings { get; private set; } = new();

    public string Wallpaper { get; private set; } = "";

    public bool Clock24 { get; private set; }

    public string Renderer { get; private set; } = "";

    public bool Failed { get; private set; }

    public IEnumerable<string> RestartKeysChanged(LunaGreeterConfig next)
    {
        if (Renderer != next.Renderer)
        {
            yield return "render.renderer";
        }
    }

    public static LunaGreeterConfig Load(string? path, BasinLogger log)
    {
        var config = new LunaGreeterConfig();
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

    public static LunaGreeterConfig Parse(string text, BasinLogger log)
    {
        var config = new LunaGreeterConfig();
        config.Read(new TomlReader(Tomlyn.Toml.ToModel(text), log));
        return config;
    }

    private void Read(TomlReader root)
    {
        Settings = GreeterSettings.Read(root);
        if (root.Section("look") is { } look)
        {
            Wallpaper = look.Text("wallpaper") ?? Wallpaper;
            Clock24 = look.Choice("clock", "12", "12", "24") == "24";
        }

        if (root.Section("render") is { } render)
        {
            Renderer = render.Text("renderer") ?? Renderer;
        }

        root.ReportUnknown();
    }
}
