using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Basin;
using Basin.Config;
using Point = Avalonia.Point;

namespace System7Wm;

internal sealed class FinderState
{
    public Box? Window { get; set; }

    public string? View { get; set; }

    public Dictionary<string, Point> Icons { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Point> SmallIcons { get; } = new(StringComparer.Ordinal);

    public static string Root => Path.Combine(Basin.Freedesktop.XdgDirectories.StateHome, "system7-wm", "finder");

    public static string FileOf(string folder) =>
        Path.Combine(Root, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16] + ".toml");

    public static FinderState Load(string folder)
    {
        var result = new FinderState();
        var file = FileOf(folder);
        if (!File.Exists(file) || TomlConfig.Read(file, out _) is not { } table ||
            !table.TryGetValue("path", out var path) || path as string != folder)
        {
            return result;
        }

        if (table.TryGetValue("window", out var window) && window is Tomlyn.Model.TomlArray { Count: 4 } box &&
            box[0] is long x && box[1] is long y && box[2] is long width && box[3] is long height)
        {
            result.Window = new Box((int)x, (int)y, (int)width, (int)height);
        }

        if (table.TryGetValue("view", out var view) && view is string mode)
        {
            result.View = mode;
        }

        Read(table, "icons", result.Icons);
        Read(table, "small_icons", result.SmallIcons);
        return result;
    }

    private static void Read(Tomlyn.Model.TomlTable table, string key, Dictionary<string, Point> into)
    {
        if (!table.TryGetValue(key, out var icons) || icons is not Tomlyn.Model.TomlTable entries)
        {
            return;
        }

        foreach (var (name, value) in entries)
        {
            if (value is Tomlyn.Model.TomlArray { Count: 2 } pair && pair[0] is long left && pair[1] is long top)
            {
                into[name] = new Point(left, top);
            }
        }
    }

    private static void Write(StringBuilder text, string key, Dictionary<string, Point> icons)
    {
        text.Append('\n').Append('[').Append(key).Append("]\n");
        foreach (var (name, point) in icons.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            text.Append(TomlValue.Key(name)).Append(" = ").Append(TomlValue.Array([TomlValue.From((long)Math.Round(point.X)), TomlValue.From((long)Math.Round(point.Y))])).Append('\n');
        }
    }

    public void Save(string folder)
    {
        var text = new StringBuilder();
        text.Append("path = ").Append(TomlValue.From(folder)).Append('\n');
        if (Window is { } box)
        {
            text.Append("window = ").Append(TomlValue.Array([TomlValue.From((long)box.X), TomlValue.From((long)box.Y), TomlValue.From((long)box.Width), TomlValue.From((long)box.Height)])).Append('\n');
        }

        if (View is { } view)
        {
            text.Append("view = ").Append(TomlValue.From(view)).Append('\n');
        }

        Write(text, "icons", Icons);
        Write(text, "small_icons", SmallIcons);

        Directory.CreateDirectory(Root);
        var file = FileOf(folder);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, text.ToString());
        File.Move(temporary, file, overwrite: true);
    }
}
