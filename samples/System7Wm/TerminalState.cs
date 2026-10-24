using System.Text;
using Basin;
using Basin.Config;

namespace System7Wm;

internal sealed class TerminalState
{
    public Box? Window { get; set; }

    public int? FontSize { get; set; }

    public static string FileOf() => Path.Combine(Basin.Freedesktop.XdgDirectories.StateHome, "system7-wm", "terminal.toml");

    public static TerminalState Load()
    {
        var result = new TerminalState();
        var file = FileOf();
        if (!File.Exists(file) || TomlConfig.Read(file, out _) is not { } table)
        {
            return result;
        }

        if (table.TryGetValue("window", out var window) && window is Tomlyn.Model.TomlArray { Count: 4 } box &&
            box[0] is long x && box[1] is long y && box[2] is long width && box[3] is long height)
        {
            result.Window = new Box((int)x, (int)y, (int)width, (int)height);
        }

        if (table.TryGetValue("font_size", out var size) && size is long points && TerminalFonts.IsSize((int)points))
        {
            result.FontSize = (int)points;
        }

        return result;
    }

    public void Save()
    {
        var text = new StringBuilder();
        if (Window is { } box)
        {
            text.Append("window = ").Append(TomlValue.Array([TomlValue.From((long)box.X), TomlValue.From((long)box.Y), TomlValue.From((long)box.Width), TomlValue.From((long)box.Height)])).Append('\n');
        }

        if (FontSize is { } size)
        {
            text.Append("font_size = ").Append(TomlValue.From((long)size)).Append('\n');
        }

        var file = FileOf();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, text.ToString());
        File.Move(temporary, file, overwrite: true);
    }
}
