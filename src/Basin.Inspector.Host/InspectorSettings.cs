using Basin.Config;

namespace Basin.Inspector;

/// <summary>
/// The settings of an <see cref="InspectorSession"/>, as an <c>[inspector]</c> TOML table gives them.
/// </summary>
public sealed record InspectorSettings
{
    /// <summary>
    /// Gets the lowercase names of the five library panels that <see cref="Read"/> accepts in <c>panels</c>.
    /// </summary>
    public static IReadOnlyList<string> LibraryPanels { get; } = ["outputs", "windows", "clients", "scene", "memory"];

    /// <summary>
    /// Gets a value indicating whether the session can build the overlay. When it is <see langword="false"/>, the session builds nothing and closes an open overlay. The default is <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether <see cref="InspectorSession.Start"/> opens the overlay. The default is <see langword="false"/>.
    /// </summary>
    public bool Open { get; init; }

    /// <summary>
    /// Gets the name of the output to open on. It is <see langword="null"/> for the <c>focused</c> choice, which lets the session pick the output.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Gets the names of the panels that show when the overlay is built. The default is the Outputs panel only.
    /// </summary>
    public IReadOnlyList<string> Panels { get; init; } = ["outputs"];

    /// <summary>
    /// Gets the ImGui font size in logical pixels, from 6 to 64. The default is 15.
    /// </summary>
    public double FontSize { get; init; } = 15;

    /// <summary>
    /// Gets the redraw rate of the live panels in frames per second, from 1 to 240. The default is 10. The session caps the slower panels lower.
    /// </summary>
    public int RefreshHz { get; init; } = 10;

    /// <summary>
    /// Reads an <c>[inspector]</c> table. An unknown panel name or a value out of range logs a warning and keeps the default.
    /// </summary>
    /// <param name="section">The table, or <see langword="null"/> when the file has none.</param>
    /// <param name="defaults">The values for absent keys, or <see langword="null"/> for the built-in defaults.</param>
    /// <param name="extraPanels">The names of the consumer's own panels that <c>panels</c> can also give. Names match without regard to case.</param>
    /// <returns>The settings. It is <paramref name="defaults"/> unchanged when <paramref name="section"/> is <see langword="null"/>.</returns>
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
