using Avalonia;

namespace Waylonia;

internal static class HostPlatform
{
    public static AppBuilder UseHostWindowing(this AppBuilder builder) => builder;
}
