using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// A type tag that gives the log level of a <see cref="LogHandler{T}"/> at compile time.
/// </summary>
public interface ILogLevelTag
{
    /// <summary>
    /// Gets the log level that the tag stands for.
    /// </summary>
    static abstract BasinLogLevel Level { get; }
}
