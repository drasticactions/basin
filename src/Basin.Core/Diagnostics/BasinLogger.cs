namespace Basin.Diagnostics;

/// <summary>
/// A logger with a category. It shares the level and the sink of <see cref="BasinLog"/>, and costs nothing for a disabled level.
/// </summary>
public readonly struct BasinLogger : IEquatable<BasinLogger>
{
    private readonly string? _category;

    internal BasinLogger(string category) => _category = category;

    /// <summary>
    /// Gets a logger with an empty category.
    /// </summary>
    public static BasinLogger None => default;

    /// <summary>
    /// Gets the category label. It is empty for the default logger.
    /// </summary>
    public string Category => _category ?? string.Empty;

    /// <summary>
    /// Tests whether a message of a level will reach the sink.
    /// </summary>
    /// <param name="level">The level to test.</param>
    /// <returns>The same result as <see cref="BasinLog.IsEnabled"/>.</returns>
    public bool IsEnabled(BasinLogLevel level) => BasinLog.IsEnabled(level);

    /// <summary>
    /// Writes a trace message.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when trace is enabled.</param>
    public void Trace(ref LogHandler<LevelTrace> message) => Emit(BasinLogLevel.Trace, ref message);

    /// <summary>
    /// Writes a debug message.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when debug is enabled.</param>
    public void Debug(ref LogHandler<LevelDebug> message) => Emit(BasinLogLevel.Debug, ref message);

    /// <summary>
    /// Writes an info message.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when info is enabled.</param>
    public void Info(ref LogHandler<LevelInfo> message) => Emit(BasinLogLevel.Info, ref message);

    /// <summary>
    /// Writes a warning.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when warnings are enabled.</param>
    public void Warn(ref LogHandler<LevelWarn> message) => Emit(BasinLogLevel.Warn, ref message);

    /// <summary>
    /// Writes an error.
    /// </summary>
    /// <param name="message">The interpolated message. It is formatted only when errors are enabled.</param>
    public void Error(ref LogHandler<LevelError> message) => Emit(BasinLogLevel.Error, ref message);

    /// <summary>
    /// Writes a trace message that ends with the type and text of an exception and of each inner exception. The stack trace follows when debug is enabled.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when trace is enabled.</param>
    public void Trace(Exception exception, ref LogHandler<LevelTrace> message) => Emit(BasinLogLevel.Trace, exception, ref message);

    /// <summary>
    /// Writes a debug message that ends with the type and text of an exception and of each inner exception. The stack trace follows when debug is enabled.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when debug is enabled.</param>
    public void Debug(Exception exception, ref LogHandler<LevelDebug> message) => Emit(BasinLogLevel.Debug, exception, ref message);

    /// <summary>
    /// Writes an info message that ends with the type and text of an exception and of each inner exception. The stack trace follows when debug is enabled.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when info is enabled.</param>
    public void Info(Exception exception, ref LogHandler<LevelInfo> message) => Emit(BasinLogLevel.Info, exception, ref message);

    /// <summary>
    /// Writes a warning that ends with the type and text of an exception and of each inner exception. The stack trace follows when debug is enabled.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when warnings are enabled.</param>
    public void Warn(Exception exception, ref LogHandler<LevelWarn> message) => Emit(BasinLogLevel.Warn, exception, ref message);

    /// <summary>
    /// Writes an error that ends with the type and text of an exception and of each inner exception. The stack trace follows when debug is enabled.
    /// </summary>
    /// <param name="exception">The exception that caused the message.</param>
    /// <param name="message">The interpolated message. It is formatted only when errors are enabled.</param>
    public void Error(Exception exception, ref LogHandler<LevelError> message) => Emit(BasinLogLevel.Error, exception, ref message);

    /// <summary>
    /// Compares the categories of two loggers with ordinal rules.
    /// </summary>
    /// <param name="other">The logger to compare with.</param>
    /// <returns><see langword="true"/> when the categories are equal.</returns>
    public bool Equals(BasinLogger other) => string.Equals(Category, other.Category, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BasinLogger other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Category);

    /// <summary>
    /// Tests whether two loggers have the same category.
    /// </summary>
    /// <param name="left">The first logger.</param>
    /// <param name="right">The second logger.</param>
    /// <returns><see langword="true"/> when the categories are equal.</returns>
    public static bool operator ==(BasinLogger left, BasinLogger right) => left.Equals(right);

    /// <summary>
    /// Tests whether two loggers have different categories.
    /// </summary>
    /// <param name="left">The first logger.</param>
    /// <param name="right">The second logger.</param>
    /// <returns><see langword="true"/> when the categories differ.</returns>
    public static bool operator !=(BasinLogger left, BasinLogger right) => !left.Equals(right);

    private void Emit<T>(BasinLogLevel level, ref LogHandler<T> message)
        where T : struct, ILogLevelTag
    {
        if (!message.Enabled)
        {
            return;
        }

        BasinLog.Sink?.Write(level, Category, message.Text);
        message.Clear();
    }

    private void Emit<T>(BasinLogLevel level, Exception exception, ref LogHandler<T> message)
        where T : struct, ILogLevelTag
    {
        if (!message.Enabled)
        {
            return;
        }

        var text = LogFormat.WithException(message.Text, exception, BasinLog.IsEnabled(BasinLogLevel.Debug));
        BasinLog.Sink?.Write(level, Category, text);
        message.Clear();
    }
}
