namespace Basin.Diagnostics;

/// <summary>
/// The tag for <see cref="BasinLogLevel.Trace"/>.
/// </summary>
public struct LevelTrace : ILogLevelTag
{
    /// <inheritdoc/>
    public static BasinLogLevel Level => BasinLogLevel.Trace;
}
