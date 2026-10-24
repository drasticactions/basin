using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// The tag for <see cref="BasinLogLevel.Debug"/>.
/// </summary>
public struct LevelDebug : ILogLevelTag
{
    /// <inheritdoc/>
    public static BasinLogLevel Level => BasinLogLevel.Debug;
}
