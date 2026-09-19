using Basin.Diagnostics;

namespace Basin.Hosted;

internal static class HostedLog
{
    internal static readonly BasinLogger Log = BasinLog.For("hosted");
}
