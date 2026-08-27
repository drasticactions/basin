namespace Waylonia;

internal static class HostSession
{
    public static string? Display { get; private set; }

    public static string? WaylandDisplay { get; private set; }

    public static void Capture()
    {
        Display = Environment.GetEnvironmentVariable("DISPLAY");
        WaylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
    }
}
