namespace Basin.Diagnostics;

/// <summary>
/// Receives the log messages that pass the <see cref="BasinLog.Level"/> threshold.
/// </summary>
public interface IBasinLogSink
{
    /// <summary>
    /// Writes one message. It can be called from any thread, and it must not keep the span after it returns.
    /// </summary>
    /// <param name="level">The severity of the message.</param>
    /// <param name="category">The logger category. It is empty for the root logger.</param>
    /// <param name="message">The formatted text.</param>
    void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message);
}
