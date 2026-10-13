using Basin.Config;
using Basin.SoftKeyboard;

namespace LunaWm;

internal sealed record KeyboardConfig
{
    public bool Enabled { get; init; } = true;

    public IReadOnlyList<string> Layouts { get; init; } = [];

    public KeyboardSize Size { get; init; } = KeyboardSize.Medium;

    public bool Sounds { get; init; } = true;

    public bool SpacesToPeriod { get; init; } = true;

    public bool Trace { get; init; } = true;

    public bool Diamonds { get; init; } = true;

    public bool Fcitx5 { get; init; } = true;

    public double ManualEdge { get; init; } = 0.15;

    public int LongPressMs { get; init; } = 350;

    public int RepeatDelayMs { get; init; } = 350;

    public int RepeatIntervalMs { get; init; } = 120;

    public KeyTiming Timing => KeyTiming.Default with
    {
        LongPressMs = LongPressMs,
        RepeatDelayMs = RepeatDelayMs,
        RepeatIntervalMs = RepeatIntervalMs,
    };

    public static KeyboardSize ParseSize(string? text, KeyboardSize fallback) => KeyboardSizes.Parse(text, fallback);

    public static string SizeName(KeyboardSize size) => KeyboardSizes.Name(size);

    public static KeyboardConfig Read(TomlReader table)
    {
        var defaults = new KeyboardConfig();
        var layouts = new List<string>();
        foreach (var id in table.Words("layouts") ?? [])
        {
            if (id == KeyboardLayouts.Xkb || KeyboardLayouts.IsBuiltIn(id) || File.Exists(Path.Combine(UserLayoutDirectory(), id + ".toml")))
            {
                layouts.Add(id);
            }
            else
            {
                table.Log.Warn($"[keyboard] layouts: no layout '{id}', ignored");
            }
        }

        return new KeyboardConfig
        {
            Enabled = table.Flag("enabled", defaults.Enabled),
            Layouts = layouts,
            Size = ParseSize(table.Choice("size", "m", "xs", "s", "m", "l"), defaults.Size),
            Sounds = table.Flag("sounds", defaults.Sounds),
            SpacesToPeriod = table.Flag("spaces2period", defaults.SpacesToPeriod),
            Trace = table.Flag("trace", defaults.Trace),
            Diamonds = table.Flag("diamonds", defaults.Diamonds),
            Fcitx5 = table.Flag("fcitx5", defaults.Fcitx5),
            ManualEdge = Math.Clamp(table.Number("manual_edge", defaults.ManualEdge), 0, 1),
            LongPressMs = Math.Clamp(table.Number("long_press_ms", defaults.LongPressMs), 50, 5000),
            RepeatDelayMs = Math.Clamp(table.Number("repeat_delay_ms", defaults.RepeatDelayMs), 50, 5000),
            RepeatIntervalMs = Math.Clamp(table.Number("repeat_interval_ms", defaults.RepeatIntervalMs), 10, 5000),
        };
    }

    public static string UserLayoutDirectory() => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "luna-wm", "layouts");
}
