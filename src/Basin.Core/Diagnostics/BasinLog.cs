namespace Basin.Diagnostics;

public static class BasinLog
{
    private static readonly BasinLogger Root = new(string.Empty);

    public static BasinLogLevel Level { get; set; } = BasinLogLevel.None;

    public static IBasinLogSink? Sink { get; set; }

    public static BasinLogger For(string category) => new(category ?? string.Empty);

    public static bool IsEnabled(BasinLogLevel level) =>
        Sink is not null && level != BasinLogLevel.None && level >= Level;

    public static void Trace(ref LogHandler<LevelTrace> message) => Root.Trace(ref message);

    public static void Debug(ref LogHandler<LevelDebug> message) => Root.Debug(ref message);

    public static void Info(ref LogHandler<LevelInfo> message) => Root.Info(ref message);

    public static void Warn(ref LogHandler<LevelWarn> message) => Root.Warn(ref message);

    public static void Error(ref LogHandler<LevelError> message) => Root.Error(ref message);
}
