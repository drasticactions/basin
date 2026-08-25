namespace Basin.Diagnostics;

public static class BasinReport
{
    private static readonly Lock Gate = new();

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

    public static void Line(string message)
    {
        lock (Gate)
        {
            var writer = Console.Out;
            writer.WriteLine(message);
            writer.Flush();
        }
    }

    public static void Flush()
    {
        lock (Gate)
        {
            Console.Out.Flush();
        }
    }
}
