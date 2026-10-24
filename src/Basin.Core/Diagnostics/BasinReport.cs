namespace Basin.Diagnostics;

/// <summary>
/// Writes machine-readable report lines to standard output, whatever the log level. Each line is written and flushed under one lock.
/// </summary>
public static class BasinReport
{
    private static readonly Lock Gate = new();

    /// <summary>
    /// Writes one interpolated line and releases its buffer.
    /// </summary>
    /// <param name="message">The interpolated line.</param>
    public static void Line(ref ReportHandler message)
    {
        lock (Gate)
        {
            var writer = Console.Out;
            writer.WriteLine(message.Text);
            writer.Flush();
        }

        message.Clear();
    }

    /// <summary>
    /// Writes one line.
    /// </summary>
    /// <param name="message">The text of the line.</param>
    public static void Line(string message)
    {
        lock (Gate)
        {
            var writer = Console.Out;
            writer.WriteLine(message);
            writer.Flush();
        }
    }

    /// <summary>
    /// Flushes standard output under the report lock.
    /// </summary>
    public static void Flush()
    {
        lock (Gate)
        {
            Console.Out.Flush();
        }
    }
}
