namespace Basin.Audio.PipeWire;

public sealed class AudioProcessTable : IAudioProcessTable
{
    public static AudioProcessTable Instance { get; } = new();

    public int ParentOf(int pid) => ProcessIdentity.ParentOf(pid);

    public int GroupOf(int pid) => ProcessIdentity.ProcessGroupOf(pid);

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
