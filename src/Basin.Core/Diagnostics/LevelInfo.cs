using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public struct LevelInfo : ILogLevelTag
{
    public static BasinLogLevel Level => BasinLogLevel.Info;
}
