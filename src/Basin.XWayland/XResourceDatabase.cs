using System.Text;

namespace Basin.XWayland;

/// <summary>
/// Edits the text of the X resource database, as the <c>RESOURCE_MANAGER</c> root property holds it.
/// </summary>
public static class XResourceDatabase
{
    /// <summary>
    /// Replaces or appends resource lines. A line whose key is in <paramref name="values"/> is dropped, and the new lines go at the end in order. Empty lines are removed.
    /// </summary>
    /// <param name="existing">The current database text, with one <c>key: value</c> line per resource.</param>
    /// <param name="values">The resources to set, for example <c>Xft.dpi</c>.</param>
    /// <returns>The new database text. Each line ends with a newline.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
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
