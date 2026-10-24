using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// The tag for <see cref="BasinLogLevel.Error"/>.
/// </summary>
public struct LevelError : ILogLevelTag
{
    /// <inheritdoc/>
    public static BasinLogLevel Level => BasinLogLevel.Error;
}
