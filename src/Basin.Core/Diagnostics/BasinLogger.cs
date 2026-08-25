namespace Basin.Diagnostics;

public readonly struct BasinLogger : IEquatable<BasinLogger>
{
    private readonly string? _category;

    internal BasinLogger(string category) => _category = category;

    public static BasinLogger None => default;

    public string Category => _category ?? string.Empty;

    public bool IsEnabled(BasinLogLevel level) => BasinLog.IsEnabled(level);

    public void Trace(ref LogHandler<LevelTrace> message) => Emit(BasinLogLevel.Trace, ref message);

    public void Debug(ref LogHandler<LevelDebug> message) => Emit(BasinLogLevel.Debug, ref message);

    public void Info(ref LogHandler<LevelInfo> message) => Emit(BasinLogLevel.Info, ref message);

    public void Warn(ref LogHandler<LevelWarn> message) => Emit(BasinLogLevel.Warn, ref message);

    public void Error(ref LogHandler<LevelError> message) => Emit(BasinLogLevel.Error, ref message);

    public bool Equals(BasinLogger other) => string.Equals(Category, other.Category, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is BasinLogger other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Category);

    public static bool operator ==(BasinLogger left, BasinLogger right) => left.Equals(right);

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
}
