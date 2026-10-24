using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// The tag for <see cref="BasinLogLevel.Info"/>.
/// </summary>
public struct LevelInfo : ILogLevelTag
{
    /// <inheritdoc/>
    public static BasinLogLevel Level => BasinLogLevel.Info;
}
