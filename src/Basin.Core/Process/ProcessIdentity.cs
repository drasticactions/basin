using System.Text;

namespace Basin;

/// <summary>
/// Facts about a process that <c>/proc</c> gives. A field that cannot be read is empty or <see langword="null"/>.
/// </summary>
/// <param name="Pid">The process ID, or 0 when it is unknown.</param>
/// <param name="Name">The command name from <c>/proc/PID/comm</c>.</param>
/// <param name="Executable">The file name of the executable, without its directory.</param>
/// <param name="SteamGame">The Steam app ID from the process environment, or <see langword="null"/>.</param>
public readonly record struct ProcessIdentity(int Pid, string Name, string Executable, uint? SteamGame)
{
    /// <summary>
    /// Reads the identity of a process from <c>/proc</c>.
    /// </summary>
    /// <param name="pid">The process ID. A value of 0 or less gives an empty identity.</param>
    /// <returns>The identity. Fields that cannot be read are empty.</returns>
    public static ProcessIdentity Read(int pid)
    {
        if (pid <= 0)
        {
            return new(0, string.Empty, string.Empty, null);
        }

        return new(pid, ReadComm(pid), ReadExecutable(pid), ReadSteamGame(pid));
    }

    /// <summary>
    /// Reads the identity of the client process that owns a surface, from its socket credentials.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The identity, or an empty identity when the surface is destroyed or the credentials are not available.</returns>
    public static ProcessIdentity Of(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.IsDestroyed || !surface.Resource.Client.TryGetCredentials(out var credentials))
        {
            return Read(0);
        }

        return Read(credentials.Pid);
    }

    /// <summary>
    /// Tests a name against the command name and the executable name, with ordinal rules.
    /// </summary>
    /// <param name="name">The name to test.</param>
    /// <returns><see langword="true"/> when one of the two names is equal to <paramref name="name"/>.</returns>
    public bool NameMatches(string name) =>
        (Name is { Length: > 0 } && string.Equals(Name, name, StringComparison.Ordinal))
        || (Executable is { Length: > 0 } && string.Equals(Executable, name, StringComparison.Ordinal));

    /// <summary>
    /// Reads the process group ID of a process from <c>/proc/PID/stat</c>.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>The process group ID, or 0 when it cannot be read.</returns>
    public static int ProcessGroupOf(int pid) => StatField(pid, 2);

    /// <summary>
    /// Reads the parent process ID of a process from <c>/proc/PID/stat</c>.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>The parent process ID, or 0 when it cannot be read.</returns>
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

    /// <summary>
    /// Finds the Steam app ID in a block of environment entries. <c>SteamGameId</c> wins over <c>SteamAppId</c>, and a value of 0 is ignored.
    /// </summary>
    /// <param name="environ">The entries, separated by NUL bytes, as <c>/proc/PID/environ</c> gives them.</param>
    /// <returns>The app ID, or <see langword="null"/> when neither variable has a value.</returns>
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
