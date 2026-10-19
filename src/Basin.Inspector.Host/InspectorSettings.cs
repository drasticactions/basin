using Basin.Config;

namespace Basin.Inspector;

public sealed record InspectorSettings
{
    public static IReadOnlyList<string> LibraryPanels { get; } = ["outputs", "windows", "clients", "scene", "memory"];

    public bool Enabled { get; init; } = true;

    public bool Open { get; init; }

    public string? Output { get; init; }

    public IReadOnlyList<string> Panels { get; init; } = ["outputs"];

    public double FontSize { get; init; } = 15;

    public int RefreshHz { get; init; } = 10;

    public static InspectorSettings Read(TomlReader? section, InspectorSettings? defaults = null, IReadOnlyCollection<string>? extraPanels = null)
    {
        var settings = defaults ?? new InspectorSettings();
        if (section is null)
        {
            return settings;
        }

        var output = section.Text("output");
        settings = settings with
        {
            Enabled = section.Flag("enabled", settings.Enabled),
            Open = section.Flag("open", settings.Open),
            Output = output is null ? settings.Output : output.Length == 0 || output == "focused" ? null : output,
        };

        if (section.Words("panels") is { } panels)
        {
            var known = new List<string>();
            foreach (var panel in panels)
            {
                if (IsKnown(panel, extraPanels))
                {
                    known.Add(panel);
                }
                else
                {
                    section.Log.Warn($"{section.Where("panels")} has no panel \"{panel}\"");
                }
            }

            settings = settings with { Panels = known };
        }

        var font = section.Number("font_size", settings.FontSize);
        if (font is >= 6 and <= 64)
        {
            settings = settings with { FontSize = font };
        }
        else
        {
            section.Log.Warn($"{section.Where("font_size")} must be from 6 to 64, not {font}");
        }

        var hz = section.Number("refresh_hz", settings.RefreshHz);
        if (hz is >= 1 and <= 240)
        {
            settings = settings with { RefreshHz = hz };
        }
        else
        {
            section.Log.Warn($"{section.Where("refresh_hz")} must be from 1 to 240, not {hz}");
        }

        return settings;
    }

    private static bool IsKnown(string panel, IReadOnlyCollection<string>? extra)
    {
        foreach (var name in LibraryPanels)
        {
            if (string.Equals(name, panel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (extra is not null)
        {
            foreach (var name in extra)
            {
                if (string.Equals(name, panel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
