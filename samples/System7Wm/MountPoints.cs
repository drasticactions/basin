using System.Text;

namespace System7Wm;

internal static class MountPoints
{
    public static string Containing(string path)
    {
        var best = "/";
        try
        {
            foreach (var line in File.ReadLines("/proc/self/mountinfo"))
            {
                var fields = line.Split(' ');
                if (fields.Length < 5)
                {
                    continue;
                }

                var mount = Unescape(fields[4]);
                if (mount.Length > best.Length && (path == mount || path.StartsWith(mount.TrimEnd('/') + "/", StringComparison.Ordinal)))
                {
                    best = mount;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System7Log.Log.Debug(error, $"finder: cannot read /proc/self/mountinfo");
        }

        return best;
    }

    private static string Unescape(string field)
    {
        if (!field.Contains('\\'))
        {
            return field;
        }

        var text = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length)
            {
                text.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                text.Append(field[i]);
            }
        }

        return text.ToString();
    }
}
