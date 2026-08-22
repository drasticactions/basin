using Basin;
using Basin.Avalonia;

namespace Waylonia;

internal static class WayloniaXWayland
{
    public static IProtocolModule? TryCreateModule() => null;

    public static void Attach(IProtocolModule module, BasinCompositorHost host, ToplevelWindows windows)
    {
    }

    public static string? DisplayName(BasinCompositorHost host) => null;
}
