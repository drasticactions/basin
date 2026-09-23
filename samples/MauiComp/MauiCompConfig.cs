using Basin.Config;
using Basin.Diagnostics;

namespace MauiComp;

internal sealed class MauiCompConfig
{
    public string Theme { get; set; } = "light";

    public string? Background { get; set; }

    public int BlurStrength { get; set; } = 5;

    public bool BlurTaskbar { get; set; }

    public bool BlurStartMenu { get; set; } = true;

    public bool BlurSwitcher { get; set; } = true;

    public bool BlurTitlebars { get; set; }

    public HashSet<string> FromFile { get; } = [];

    public HashSet<string> FromFlags { get; } = [];

    public static string DefaultPath() => TomlConfig.DefaultPath("maui-comp");

    public static MauiCompConfig Load(string? path, BasinLogger log, out string? fatal)
    {
        var config = new MauiCompConfig();
        fatal = null;
        if (path == "false")
        {
            return config;
        }

        var named = path is { Length: > 0 };
        var file = named ? path! : DefaultPath();
        if (!named && !File.Exists(file))
        {
            Seed(file, log);
        }

        var table = TomlConfig.Read(file, out var failure);
        if (table is null)
        {
            if (named)
            {
                fatal = $"{file}: {failure}";
            }
            else if (File.Exists(file))
            {
                log.Warn($"{file} did not parse, keeping defaults: {failure}");
            }

            return config;
        }

        config.Apply(new TomlReader(table, log));
        return config;
    }

    public static string Template()
    {
        using var stream = typeof(MauiCompConfig).Assembly.GetManifestResourceStream("maui-comp.toml")
            ?? throw new InvalidOperationException("maui-comp.toml is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Seed(string file, BasinLogger log)
    {
        try
        {
            if (Path.GetDirectoryName(file) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
            {
                using var writer = new StreamWriter(stream);
                writer.Write(Template());
            }

            log.Info($"wrote the default configuration to {file}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"cannot write {file}, keeping the built-in defaults: {error.Message}");
        }
    }

    private void Apply(TomlReader reader)
    {
        if (reader.Table.ContainsKey("theme"))
        {
            FromFile.Add("theme");
        }

        if (reader.Table.ContainsKey("background"))
        {
            FromFile.Add("background");
        }

        Theme = reader.Choice("theme", Theme, "light", "dark");
        Background = reader.Text("background") ?? Background;
        if (reader.Section("blur") is { } blur)
        {
            var strength = blur.Number("strength", BlurStrength);
            BlurStrength = Math.Clamp(strength, 1, Basin.Effects.BlurStrength.Steps);
            if (strength != BlurStrength)
            {
                reader.Log.Warn($"[blur] strength is a strength from 1 to {Basin.Effects.BlurStrength.Steps}, not a pixel radius; {strength} reads as {BlurStrength}");
            }

            BlurTaskbar = blur.Flag("taskbar", BlurTaskbar);
            BlurStartMenu = blur.Flag("start_menu", BlurStartMenu);
            BlurSwitcher = blur.Flag("switcher", BlurSwitcher);
            BlurTitlebars = blur.Flag("titlebars", BlurTitlebars);
        }

        reader.ReportUnknown();
    }
}
