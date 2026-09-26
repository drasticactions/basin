using Basin.Diagnostics;

namespace Basin.Cli;

public static class CompositorLines
{
    public static string Socket(string? name) =>
        string.IsNullOrEmpty(name) ? "SOCKET (inherited)" : $"SOCKET {name}";

    public static string Frames(long rendered) =>
        $"FRAMES {rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}";
}
