using System.Text;
using System.Text.Json;

namespace PlasmaHost;

internal static class KwinTiling
{
    private const string Group = "Tiling";

    public static PlasmaTile Load(string screen)
    {
        var raw = KdeIni.ReadEntry(KdeIni.ConfigPath("kwinrc"), $"{Group}][{screen}", "tiles")
            ?? KdeIni.ReadEntry(KdeIni.ConfigPath("kwinrc"), Group, "tiles");
        if (raw is not { Length: > 0 })
        {
            return PlasmaTile.Default();
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return Read(document.RootElement, horizontal: true);
        }
        catch (JsonException)
        {
            return PlasmaTile.Default();
        }
    }

    public static string Serialize(PlasmaTile root)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, root, root.Horizontal, fraction: null);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static bool Save(string screen, PlasmaTile root)
    {
        var path = KdeIni.ConfigPath("kwinrc");
        if (path is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            var lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : [];
            var header = $"[{Group}][{screen}]";
            var entry = $"tiles={Serialize(root)}";
            var index = lines.FindIndex(line => line.Trim() == header);
            if (index < 0)
            {
                lines.Add(string.Empty);
                lines.Add(header);
                lines.Add(entry);
            }
            else
            {
                var replaced = false;
                for (var i = index + 1; i < lines.Count && !lines[i].StartsWith('['); i++)
                {
                    if (lines[i].StartsWith("tiles=", StringComparison.Ordinal))
                    {
                        lines[i] = entry;
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                {
                    lines.Insert(index + 1, entry);
                }
            }

            File.WriteAllLines(path, lines);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static PlasmaTile Read(JsonElement element, bool horizontal)
    {
        var direction = horizontal;
        if (element.TryGetProperty("layoutDirection", out var value) && value.ValueKind == JsonValueKind.String)
        {
            direction = value.GetString() != "vertical";
        }

        var tile = new PlasmaTile(direction);
        if (element.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number)
        {
            tile.Fraction = width.GetDouble();
        }
        else if (element.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number)
        {
            tile.Fraction = height.GetDouble();
        }

        if (element.TryGetProperty("tiles", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                tile.Children.Add(Read(child, direction));
            }
        }

        return tile;
    }

    private static void Write(Utf8JsonWriter writer, PlasmaTile tile, bool parentHorizontal, double? fraction)
    {
        writer.WriteStartObject();
        if (fraction is { } value)
        {
            writer.WriteNumber(parentHorizontal ? "width" : "height", Math.Round(value, 4));
        }

        if (tile.Children.Count > 0)
        {
            writer.WriteString("layoutDirection", tile.Horizontal ? "horizontal" : "vertical");
            writer.WriteStartArray("tiles");
            foreach (var child in tile.Children)
            {
                Write(writer, child, tile.Horizontal, child.Fraction);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}
