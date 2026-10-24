namespace Basin.Audio.PipeWire;

/// <summary>
/// The default <see cref="IAudioProcessTable"/>. It reads <c>/proc</c> on Linux.
/// </summary>
public sealed class AudioProcessTable : IAudioProcessTable
{
    /// <summary>
    /// Gets the shared instance. The table keeps no state.
    /// </summary>
    public static AudioProcessTable Instance { get; } = new();

    /// <inheritdoc/>
    public int ParentOf(int pid) => ProcessIdentity.ParentOf(pid);

    /// <inheritdoc/>
    public int GroupOf(int pid) => ProcessIdentity.ProcessGroupOf(pid);

    /// <summary>
    /// Reads the <c>name</c> key of the <c>[Application]</c> group in <c>/proc/PID/root/.flatpak-info</c>.
    /// </summary>
    /// <param name="pid">The process to look up.</param>
    /// <returns>The Flatpak application id, or <see langword="null"/> when <paramref name="pid"/> is 0 or less, or when the file is absent, unreadable or has no name.</returns>
    public string? FlatpakAppOf(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText($"/proc/{pid}/root/.flatpak-info");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var inApplication = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inApplication = line == "[Application]";
                continue;
            }

            if (inApplication && line.StartsWith("name=", StringComparison.Ordinal))
            {
                return line["name=".Length..].Trim();
            }
        }

        return null;
    }
}
