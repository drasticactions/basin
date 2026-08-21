using Basin.Capabilities;

namespace Basin.Seat;

public static class SystemKeymap
{
    private static readonly string[] EnvironmentNames =
    [
        "XKB_DEFAULT_RULES", "XKB_DEFAULT_MODEL", "XKB_DEFAULT_LAYOUT",
        "XKB_DEFAULT_VARIANT", "XKB_DEFAULT_OPTIONS",
    ];

    public static KeymapNames Read() => Read("/");

    public static KeymapNames Read(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var name in EnvironmentNames)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                return default;
            }
        }

        return FromXorgConfig(Path.Combine(root, "etc/X11/xorg.conf.d/00-keyboard.conf"))
            ?? FromShellFile(Path.Combine(root, "etc/default/keyboard"))
            ?? FromShellFile(Path.Combine(root, "etc/vconsole.conf"))
            ?? FromConsoleKeymap(root)
            ?? default;
    }

    private static KeymapNames? FromXorgConfig(string path)
    {
        if (ReadLines(path) is not { } lines)
        {
            return null;
        }

        string? layout = null, model = null, variant = null, options = null;
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (!text.StartsWith("Option", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = text.Split('"', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var value = parts[2];
            switch (parts[1].ToLowerInvariant())
            {
                case "xkblayout": layout = value; break;
                case "xkbmodel": model = value; break;
                case "xkbvariant": variant = value; break;
                case "xkboptions": options = value; break;
            }
        }

        return layout is { Length: > 0 } ? new KeymapNames(null, Empty(model), layout, Empty(variant), Empty(options)) : null;
    }

    private static KeymapNames? FromShellFile(string path)
    {
        if (ReadLines(path) is not { } lines)
        {
            return null;
        }

        var values = ShellValues(lines);
        return values.GetValueOrDefault("XKBLAYOUT") is { Length: > 0 } layout
            ? new KeymapNames(
                null,
                Empty(values.GetValueOrDefault("XKBMODEL")),
                layout,
                Empty(values.GetValueOrDefault("XKBVARIANT")),
                Empty(values.GetValueOrDefault("XKBOPTIONS")))
            : null;
    }

    private static KeymapNames? FromConsoleKeymap(string root)
    {
        if (ReadLines(Path.Combine(root, "etc/vconsole.conf")) is not { } lines ||
            ShellValues(lines).GetValueOrDefault("KEYMAP") is not { Length: > 0 } keymap ||
            ReadLines(Path.Combine(root, "usr/share/systemd/kbd-model-map")) is not { } table)
        {
            return null;
        }

        foreach (var line in table)
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (columns.Length >= 2 && columns[0] == keymap)
            {
                return new KeymapNames(
                    null,
                    Column(columns, 2),
                    columns[1],
                    Column(columns, 3),
                    Column(columns, 4));
            }
        }

        return null;
    }

    private static string? Column(string[] columns, int index) =>
        index < columns.Length && columns[index] != "-" ? columns[index] : null;

    private static string? Empty(string? value) => value is { Length: > 0 } ? value : null;

    private static Dictionary<string, string> ShellValues(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var text = line.Trim();
            var equals = text.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || text.StartsWith('#'))
            {
                continue;
            }

            values[text[..equals].Trim()] = text[(equals + 1)..].Trim().Trim('"', '\'');
        }

        return values;
    }

    private static string[]? ReadLines(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
