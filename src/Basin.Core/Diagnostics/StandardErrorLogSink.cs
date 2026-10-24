namespace Basin.Diagnostics;

/// <summary>
/// A log sink that writes <c>[level] category: message</c> lines to standard error. It is safe to use from many threads.
/// </summary>
public sealed class StandardErrorLogSink : IBasinLogSink
{
    private readonly Lock _gate = new();

    /// <inheritdoc/>
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

    /// <summary>
    /// Flushes standard error under the sink lock.
    /// </summary>
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
