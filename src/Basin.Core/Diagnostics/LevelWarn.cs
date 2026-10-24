using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// The tag for <see cref="BasinLogLevel.Warn"/>.
/// </summary>
public struct LevelWarn : ILogLevelTag
{
    /// <inheritdoc/>
    public static BasinLogLevel Level => BasinLogLevel.Warn;
}
