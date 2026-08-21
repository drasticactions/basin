using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public struct LevelDebug : ILogLevelTag
{
    public static BasinLogLevel Level => BasinLogLevel.Debug;
}
