namespace Basin.Diagnostics;

public sealed class StandardErrorLogSink : IBasinLogSink
{
    private readonly Lock _gate = new();

    public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message)
    {
        lock (_gate)
        {
            var writer = Console.Error;
            writer.Write('[');
            writer.Write(Label(level));
            writer.Write("] ");
            if (!string.IsNullOrEmpty(category))
            {
                writer.Write(category);
                writer.Write(": ");
            }

            writer.WriteLine(message);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            Console.Error.Flush();
        }
    }

    private static string Label(BasinLogLevel level) => level switch
    {
        BasinLogLevel.Trace => "trace",
        BasinLogLevel.Debug => "debug",
        BasinLogLevel.Info => "info",
        BasinLogLevel.Warn => "warn",
        BasinLogLevel.Error => "error",
        _ => "none",
    };
}
