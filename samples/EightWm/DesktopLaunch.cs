using Basin.Freedesktop;

namespace EightWm;

internal static class DesktopLaunch
{
    public static string? CommandFor(DesktopEntry entry) =>
        entry.LaunchArgv(ExecLine.TerminalFromEnvironment()) is { } argv ? ExecLine.Join(argv) : null;
}
