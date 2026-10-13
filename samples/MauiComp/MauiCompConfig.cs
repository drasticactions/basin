using Basin.Config;
using Basin.Diagnostics;

namespace MauiComp;

internal sealed class MauiCompConfig
{
    public string Theme { get; set; } = "light";

    public HandoffSettings Handoff { get; set; } = new();

    public string? Background { get; set; }

    public int BlurStrength { get; set; } = 5;

    public bool BlurTaskbar { get; set; }

    public bool BlurStartMenu { get; set; } = true;

    public bool BlurSwitcher { get; set; } = true;

    public bool BlurTitlebars { get; set; }

    public IReadOnlyList<Basin.Capabilities.InputRule> InputRules { get; private set; } = [];

    public HashSet<string> FromFile { get; } = [];

    public HashSet<string> FromFlags { get; } = [];

    public static string DefaultPath() => TomlConfig.DefaultPath("maui-comp");

    public static MauiCompConfig Load(string? path, BasinLogger log, out string? fatal)
    {
        var config = new MauiCompConfig();
        var load = TomlConfig.Load(path == "false" ? null : path, "maui-comp", path == "false", TemplateStream, log);
        fatal = load.Fatal;
        if (load.Table is { } table)
        {
            config.Apply(new TomlReader(table, log));
        }

        return config;
    }

    public static string Template()
    {
        using var reader = new StreamReader(TemplateStream());
        return reader.ReadToEnd();
    }

    private static Stream TemplateStream() =>
        typeof(MauiCompConfig).Assembly.GetManifestResourceStream("maui-comp.toml")
            ?? throw new InvalidOperationException("maui-comp.toml is not embedded");

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

        if (reader.Section("input") is { } input)
        {
            InputRules = InputConfig.Read(input, reader.Log);
        }

        Handoff = HandoffSettings.Read(reader.Section("handoff"));
        reader.ReportUnknown();
    }
}
