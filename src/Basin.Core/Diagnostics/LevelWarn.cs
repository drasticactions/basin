using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public struct LevelWarn : ILogLevelTag
{
    public static BasinLogLevel Level => BasinLogLevel.Warn;
}
