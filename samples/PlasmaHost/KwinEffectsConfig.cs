using Basin.Effects;

namespace PlasmaHost;

internal sealed class KwinEffectsConfig
{
    private readonly string _kwinrc;
    private readonly string _kdeglobals;

    private KwinEffectsConfig(string kwinrc, string kdeglobals)
    {
        _kwinrc = kwinrc;
        _kdeglobals = kdeglobals;
        DurationFactor = Read(_kdeglobals, "KDE", "AnimationDurationFactor") is { } raw &&
            double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var factor)
            ? Math.Max(0, factor)
            : 1.0;
    }

    public static KwinEffectsConfig Load() =>
        new(KdeIni.ConfigPath("kwinrc"), KdeIni.ConfigPath("kdeglobals"));

    public double DurationFactor { get; }

    public bool IsEnabled(string plugin, bool byDefault) =>
        Read(_kwinrc, "Plugins", $"{plugin}Enabled") is { } raw ? Flag(raw, byDefault) : byDefault;

    public double Number(string effect, string key, double fallback) =>
        Read(_kwinrc, $"Effect-{effect}", key) is { } raw &&
        double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public int Integer(string effect, string key, int fallback) =>
        Read(_kwinrc, $"Effect-{effect}", key) is { } raw && int.TryParse(raw, out var value) ? value : fallback;

    public bool Boolean(string effect, string key, bool fallback) =>
        Read(_kwinrc, $"Effect-{effect}", key) is { } raw ? Flag(raw, fallback) : fallback;

    public AnimationDuration Duration(double baseMillis) => new(baseMillis, DurationFactor);

    private static bool Flag(string raw, bool fallback) => raw.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" => false,
        _ => fallback,
    };

    private static string? Read(string path, string group, string key) => KdeIni.ReadEntry(path, group, key);
}
