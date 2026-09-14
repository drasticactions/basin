using Basin.Diagnostics;

namespace Basin.Freedesktop;

internal static class KeyFile
{
    private static readonly BasinLogger Log = BasinLog.For("freedesktop");

    public static List<KeyFileGroup>? Read(string path)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"{path}: unreadable ({error.Message})");
            return null;
        }

        return Parse(lines, path);
    }

    public static List<KeyFileGroup> Parse(IReadOnlyList<string> lines, string path)
    {
        var groups = new List<KeyFileGroup>();
        KeyFileGroup? current = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].AsSpan().TrimEnd();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (line[^1] != ']')
                {
                    Log.Debug($"{path}:{i + 1}: unterminated group header, skipped");
                    current = null;
                    continue;
                }

                current = new KeyFileGroup(line[1..^1].ToString());
                groups.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals < 0)
            {
                Log.Debug($"{path}:{i + 1}: no '=' on the line, skipped");
                continue;
            }

            var key = line[..equals].TrimEnd();
            var value = line[(equals + 1)..].TrimStart();
            string? suffix = null;
            var bracket = key.IndexOf('[');
            if (bracket >= 0 && key[^1] == ']')
            {
                suffix = key[(bracket + 1)..^1].ToString();
                key = key[..bracket];
            }

            current.Entries.Add(new KeyFileEntry(key.ToString(), suffix, value.ToString()));
        }

        return groups;
    }
}
