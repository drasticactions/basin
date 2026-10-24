namespace Basin.Diagnostics;

/// <summary>
/// The severity of a log message, from the most detailed to the most severe. <see cref="None"/> turns logging off.
/// </summary>
public enum BasinLogLevel
{
    /// <summary>
    /// Very detailed messages, for example one per protocol request.
    /// </summary>
    Trace,
    /// <summary>
    /// Detailed messages for debugging.
    /// </summary>
    Debug,
    /// <summary>
    /// Normal operational messages.
    /// </summary>
    Info,
    /// <summary>
    /// Unusual conditions that the compositor can continue after.
    /// </summary>
    Warn,
    /// <summary>
    /// Failures.
    /// </summary>
    Error,
    /// <summary>
    /// No messages. As a threshold, it turns logging off.
    /// </summary>
    None,
}
