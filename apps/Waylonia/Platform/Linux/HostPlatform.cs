using Avalonia;
using Avalonia.Wayland;

namespace Waylonia;

internal static class HostPlatform
{
#pragma warning disable AVALONIA_WAYLAND_FORCE_CSD
    public static AppBuilder UseHostWindowing(this AppBuilder builder) => builder
        .UseWaylandWithFallback()
        .With(new WaylandPlatformOptions { ForceDrawnDecorations = true });
#pragma warning restore AVALONIA_WAYLAND_FORCE_CSD
}
