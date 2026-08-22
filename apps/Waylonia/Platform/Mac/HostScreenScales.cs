using Basin.Avalonia;

namespace Waylonia;

internal static class HostScreenScales
{
    public static double? TryGetScale(HostScreenInfo info) =>
        OperatingSystem.IsMacOS() ? MacScreenScales.TryGetScale(info) : null;
}
