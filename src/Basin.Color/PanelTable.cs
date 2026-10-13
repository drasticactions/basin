using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;

namespace Basin.Color;

public sealed class PanelTable
{
    private readonly List<PanelEntry> _entries;

    public PanelTable(IEnumerable<PanelEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = [.. entries];
    }

    public static PanelTable Empty { get; } = new([]);

    public IReadOnlyList<PanelEntry> Entries => _entries;

    public string? Path { get; private init; }

    public static string DefaultPath =>
        System.IO.Path.Combine(Basin.Freedesktop.XdgDirectories.ConfigHome, "basin", "panels.toml");

    public static PanelTable Load(BasinLogger log, string? path = null)
    {
        path ??= DefaultPath;
        if (TomlConfig.Read(path, log) is not { } table)
        {
            return new PanelTable([]) { Path = path };
        }

        var reader = new TomlReader(table, log);
        var parsed = Parse(reader);
        reader.ReportUnknown();
        return new PanelTable(parsed._entries) { Path = path };
    }

    public static PanelTable Parse(TomlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var entries = new List<PanelEntry>();
        foreach (var panel in reader.Sections("panel"))
        {
            if (panel.Text("vendor") is not { Length: > 0 } vendor)
            {
                panel.Log.Warn($"{panel.Where("vendor")} is required, the entry is skipped");
                continue;
            }

            var product = panel.Raw("product") switch
            {
                long number => (int?)number,
                string text when text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex) => hex,
                _ => null,
            };
            entries.Add(new PanelEntry
            {
                Vendor = vendor,
                Product = product,
                Serial = panel.Text("serial"),
                Orientation = panel.Text("orientation") switch
                {
                    "normal" => OutputTransform.Normal,
                    "90" => OutputTransform.Rotate90,
                    "180" => OutputTransform.Rotate180,
                    "270" => OutputTransform.Rotate270,
                    null => null,
                    var other => Warn<OutputTransform?>(panel, "orientation", other, "normal, 90, 180 or 270"),
                },
                Colorimetry = ReadColorimetry(panel),
                MaxNits = Optional(panel, "max_nits"),
                MaxFrameAverageNits = Optional(panel, "max_frame_average_nits"),
                MinNits = Optional(panel, "min_nits"),
                Hdr = panel.Raw("hdr") as bool?,
                OutputCurve = panel.Text("output_curve") switch
                {
                    "srgb" => ColorTransferFunction.Srgb,
                    "gamma22" => ColorTransferFunction.Gamma22,
                    null => null,
                    var other => Warn<ColorTransferFunction?>(panel, "output_curve", other, "srgb or gamma22"),
                },
                RefreshMilliHz = panel.Numbers("refresh") is { } rates
                    ? [.. rates.Select(rate => (int)Math.Round(rate * 1000))]
                    : [],
                ModeGenerator = panel.Text("mode_generator") switch
                {
                    "cvt" => OutputModeGenerator.Cvt,
                    "same_timing" => OutputModeGenerator.SameTiming,
                    "scaled_clock" => OutputModeGenerator.ScaledClock,
                    null => null,
                    var other => Warn<OutputModeGenerator?>(panel, "mode_generator", other, "cvt, same_timing or scaled_clock"),
                },
                MaxClockKhz = panel.Raw("max_clock_khz") is long clock ? (int)clock : null,
            });
        }

        return new PanelTable(entries);
    }

    public PanelEntry? Match(in PanelIdentity panel)
    {
        PanelEntry? best = null;
        foreach (var entry in _entries)
        {
            if (entry.Matches(panel) && (best is null || entry.Specificity > best.Specificity))
            {
                best = entry;
            }
        }

        return best;
    }

    public PanelEntry? Match(IOutput output) => Match(PanelIdentity.Of(output));

    private static double? Optional(TomlReader panel, string key) => panel.Raw(key) switch
    {
        long whole => whole,
        double fractional => fractional,
        _ => null,
    };

    private static T Warn<T>(TomlReader panel, string key, string value, string allowed)
    {
        panel.Log.Warn($"{panel.Where(key)} = \"{value}\" is not {allowed}, the key is ignored");
        return default!;
    }

    private static (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? ReadColorimetry(TomlReader panel)
    {
        if (panel.Section("colorimetry") is not { } colorimetry)
        {
            return null;
        }

        var red = colorimetry.Numbers("red");
        var green = colorimetry.Numbers("green");
        var blue = colorimetry.Numbers("blue");
        var white = colorimetry.Numbers("white");
        if (red is not [var rx, var ry] || green is not [var gx, var gy] || blue is not [var bx, var by] ||
            white is not [var wx, var wy])
        {
            panel.Log.Warn($"{panel.Where("colorimetry")} needs red, green, blue and white as [x, y], the key is ignored");
            return null;
        }

        return (rx, ry, gx, gy, bx, by, wx, wy);
    }

}
