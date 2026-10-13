using System.Text;

namespace Basin.XWayland;

public static class XResourceDatabase
{
    public static string Merge(string existing, IReadOnlyList<(string Key, string Value)> values)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(values);
        var result = new StringBuilder(existing.Length + 64);
        foreach (var raw in existing.Split('\n'))
        {
            var line = raw.TrimEnd('\r', '\0');
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var key = colon > 0 ? line[..colon].Trim() : string.Empty;
            if (key.Length > 0 && Contains(values, key))
            {
                continue;
            }

            result.Append(line).Append('\n');
        }

        foreach (var (key, value) in values)
        {
            result.Append(key).Append(":\t").Append(value).Append('\n');
        }

        return result.ToString();

        static bool Contains(IReadOnlyList<(string Key, string Value)> values, string key)
        {
            foreach (var (candidate, _) in values)
            {
                if (string.Equals(candidate, key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
