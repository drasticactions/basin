using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public static class BasinLog
{
    public static BasinLogLevel Level { get; set; } = BasinLogLevel.None;

    public static Action<BasinLogLevel, string>? Sink { get; set; }

    public static bool IsEnabled(BasinLogLevel level) =>
        Sink is not null && level != BasinLogLevel.None && level >= Level;

    public static void Debug(ref LogHandler<LevelDebug> message) => Emit(BasinLogLevel.Debug, ref message);

    public static void Info(ref LogHandler<LevelInfo> message) => Emit(BasinLogLevel.Info, ref message);

    public static void Warn(ref LogHandler<LevelWarn> message) => Emit(BasinLogLevel.Warn, ref message);

    public static void Error(ref LogHandler<LevelError> message) => Emit(BasinLogLevel.Error, ref message);

    private static void Emit<T>(BasinLogLevel level, ref LogHandler<T> message)
        where T : struct, ILogLevelTag
    {
        if (message.Enabled)
        {
            Sink!(level, message.ToStringAndClear());
        }
    }
}
