using Basin.Diagnostics;

namespace Basin.Host;

internal static class HostLog
{
    internal static readonly BasinLogger Log = BasinLog.For("host");
}
