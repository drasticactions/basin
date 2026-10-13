using System.Text;

namespace Basin;

public readonly record struct ProcessIdentity(int Pid, string Name, string Executable, uint? SteamGame)
{
    public static ProcessIdentity Read(int pid)
    {
        if (pid <= 0)
        {
            return new(0, string.Empty, string.Empty, null);
        }

        return new(pid, ReadComm(pid), ReadExecutable(pid), ReadSteamGame(pid));
    }

    public static ProcessIdentity Of(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.IsDestroyed || !surface.Resource.Client.TryGetCredentials(out var credentials))
        {
            return Read(0);
        }

        return Read(credentials.Pid);
    }

    public bool NameMatches(string name) =>
        (Name is { Length: > 0 } && string.Equals(Name, name, StringComparison.Ordinal))
        || (Executable is { Length: > 0 } && string.Equals(Executable, name, StringComparison.Ordinal));

    public static int ProcessGroupOf(int pid) => StatField(pid, 2);

    public static int ParentOf(int pid) => StatField(pid, 1);

    private static int StatField(int pid, int index)
    {
        if (pid <= 0)
        {
            return 0;
        }

        string stat;
        try
        {
            stat = File.ReadAllText($"/proc/{pid}/stat");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var close = stat.LastIndexOf(')');
        if (close < 0 || close + 2 > stat.Length)
        {
            return 0;
        }

        var fields = stat[(close + 2)..].Split(' ');
        return fields.Length > index && int.TryParse(fields[index], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string ReadComm(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").TrimEnd('\n');
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string ReadExecutable(int pid)
    {
        try
        {
            var target = File.ResolveLinkTarget($"/proc/{pid}/exe", returnFinalTarget: false)?.FullName;
            if (string.IsNullOrEmpty(target))
            {
                return string.Empty;
            }

            const string deleted = " (deleted)";
            if (target.EndsWith(deleted, StringComparison.Ordinal))
            {
                target = target[..^deleted.Length];
            }

            return Path.GetFileName(target);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static uint? ReadSteamGame(int pid)
    {
        byte[] environ;
        try
        {
            environ = File.ReadAllBytes($"/proc/{pid}/environ");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return SteamGameFrom(environ);
    }

    public static uint? SteamGameFrom(ReadOnlySpan<byte> environ)
    {
        uint? appId = null;
        while (!environ.IsEmpty)
        {
            var end = environ.IndexOf((byte)0);
            var entry = end < 0 ? environ : environ[..end];
            environ = end < 0 ? [] : environ[(end + 1)..];
            if (Value(entry, "SteamGameId="u8) is { } game)
            {
                return game;
            }

            appId ??= Value(entry, "SteamAppId="u8);
        }

        return appId;

        static uint? Value(ReadOnlySpan<byte> entry, ReadOnlySpan<byte> prefix)
        {
            if (!entry.StartsWith(prefix))
            {
                return null;
            }

            return uint.TryParse(Encoding.ASCII.GetString(entry[prefix.Length..]), out var value) && value != 0
                ? value
                : null;
        }
    }
}
