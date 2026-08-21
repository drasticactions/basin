using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public interface ILogLevelTag
{
    static abstract BasinLogLevel Level { get; }
}
