using static Basin.Greetd.GreetdLog;

namespace Basin.Greetd;

public static class LoginUsers
{
    public const uint DefaultMinUid = 1000;

    public const uint DefaultMaxUid = 60000;

    public static IReadOnlyList<LoginUser> Read(LoginUsersOptions? options = null)
    {
        options ??= new LoginUsersOptions();
        var (min, max) = UidRange(options);
        var users = new List<LoginUser>();
        string[] lines;
        try
        {
            lines = File.ReadAllLines(options.PasswdPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"{options.PasswdPath}: not read ({error.Message}); no user is listed");
            return users;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (Parse(line) is not { } user)
            {
                continue;
            }

            var homed = options.HomedUids?.Contains(user.Uid) == true;
            if ((user.Uid < min || user.Uid > max) && !homed)
            {
                continue;
            }

            if (Path.GetFileName(user.Shell) is "false" or "nologin")
            {
                continue;
            }

            if (options.Hide?.Contains(user.Name) == true || !seen.Add(user.Name))
            {
                continue;
            }

            users.Add(user with { IconPath = Icon(options.IconDirectory, user.Name) });
        }

        users.Sort(static (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return users;
    }

    public static (uint Min, uint Max) UidRange(LoginUsersOptions? options = null)
    {
        options ??= new LoginUsersOptions();
        uint min = DefaultMinUid;
        uint max = DefaultMaxUid;
        try
        {
            foreach (var raw in File.ReadLines(options.LoginDefsPath))
            {
                var line = raw.AsSpan().Trim();
                if (line.IsEmpty || line[0] == '#')
                {
                    continue;
                }

                var split = line.IndexOfAny(' ', '\t');
                if (split <= 0)
                {
                    continue;
                }

                var key = line[..split];
                var value = line[split..].Trim();
                if (key.SequenceEqual("UID_MIN") && uint.TryParse(value, out var parsedMin))
                {
                    min = parsedMin;
                }
                else if (key.SequenceEqual("UID_MAX") && uint.TryParse(value, out var parsedMax))
                {
                    max = parsedMax;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (error is not FileNotFoundException and not DirectoryNotFoundException)
            {
                Log.Info($"{options.LoginDefsPath}: not read ({error.Message}); UID range {min}-{max}");
            }
        }

        return (options.MinUid != 0 ? options.MinUid : min, options.MaxUid != 0 ? options.MaxUid : max);
    }

    internal static LoginUser? Parse(string line)
    {
        if (line.Length == 0 || line[0] == '#' || line[0] == '+' || line[0] == '-')
        {
            return null;
        }

        var fields = line.Split(':');
        if (fields.Length < 7 || fields[0].Length == 0 || !uint.TryParse(fields[2], out var uid))
        {
            return null;
        }

        var gecos = fields[4];
        var comma = gecos.IndexOf(',');
        var realName = (comma >= 0 ? gecos[..comma] : gecos).Trim();
        return new LoginUser(fields[0], uid, realName, fields[5], fields[6], null);
    }

    private static string? Icon(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
