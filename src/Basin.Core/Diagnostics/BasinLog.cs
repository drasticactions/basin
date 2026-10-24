namespace Basin.Diagnostics;

/// <summary>
/// The process-wide log configuration and the root logger. Messages below <see cref="Level"/> cost no formatting and no allocation.
/// </summary>
public static class BasinLog
{
    private static readonly BasinLogger Root = new(string.Empty);

    /// <summary>
    /// Gets or sets the lowest level that reaches the sink. The default is <see cref="BasinLogLevel.None"/>.
    /// </summary>
    public static BasinLogLevel Level { get; set; } = BasinLogLevel.None;

    /// <summary>
    /// Gets or sets the destination of log messages. With no sink, every level is off.
    /// </summary>
    public static IBasinLogSink? Sink { get; set; }

    /// <summary>
    /// Makes a logger that labels its messages with a category.
    /// </summary>
    /// <param name="category">The category label. <see langword="null"/> becomes empty.</param>
    /// <returns>A logger that shares the process-wide level and sink.</returns>
    public static BasinLogger For(string category) => new(category ?? string.Empty);

    /// <summary>
    /// Tests whether a message of a level will reach the sink.
    /// </summary>
    /// <param name="level">The level to test.</param>
    /// <returns><see langword="true"/> when a sink is set and the level is at or above the threshold.</returns>
    public static bool IsEnabled(BasinLogLevel level) =>
        Sink is not null && level != BasinLogLevel.None && level >= Level;

    /// <summary>
    /// Writes a trace message with no category.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when trace is enabled.</param>
    public static void Trace(ref LogHandler<LevelTrace> message) => Root.Trace(ref message);

    /// <summary>
    /// Writes a debug message with no category.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when debug is enabled.</param>
    public static void Debug(ref LogHandler<LevelDebug> message) => Root.Debug(ref message);

    /// <summary>
    /// Writes an info message with no category.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when info is enabled.</param>
    public static void Info(ref LogHandler<LevelInfo> message) => Root.Info(ref message);

    /// <summary>
    /// Writes a warning with no category.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when warnings are enabled.</param>
    public static void Warn(ref LogHandler<LevelWarn> message) => Root.Warn(ref message);

    /// <summary>
    /// Writes an error with no category.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when errors are enabled.</param>
    public static void Error(ref LogHandler<LevelError> message) => Root.Error(ref message);

    /// <summary>
    /// Writes a trace message with no category, followed by the type and text of an exception and of each inner exception.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when trace is enabled.</param>
    public static void Trace(Exception exception, ref LogHandler<LevelTrace> message) => Root.Trace(exception, ref message);

    /// <summary>
    /// Writes a debug message with no category, followed by the type and text of an exception and of each inner exception.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when debug is enabled.</param>
    public static void Debug(Exception exception, ref LogHandler<LevelDebug> message) => Root.Debug(exception, ref message);

    /// <summary>
    /// Writes an info message with no category, followed by the type and text of an exception and of each inner exception.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when info is enabled.</param>
    public static void Info(Exception exception, ref LogHandler<LevelInfo> message) => Root.Info(exception, ref message);

    /// <summary>
    /// Writes a warning with no category, followed by the type and text of an exception and of each inner exception.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when warnings are enabled.</param>
    public static void Warn(Exception exception, ref LogHandler<LevelWarn> message) => Root.Warn(exception, ref message);

    /// <summary>
    /// Writes an error with no category, followed by the type and text of an exception and of each inner exception.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when errors are enabled.</param>
    public static void Error(Exception exception, ref LogHandler<LevelError> message) => Root.Error(exception, ref message);
}
