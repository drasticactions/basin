using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public struct LevelError : ILogLevelTag
{
    public static BasinLogLevel Level => BasinLogLevel.Error;
}
