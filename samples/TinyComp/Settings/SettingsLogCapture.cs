using Basin.Diagnostics;

namespace TinyComp;

internal sealed class SettingsLogCapture(IBasinLogSink? forward, string captured) : IBasinLogSink
{
    public List<string> Warnings { get; } = [];

    public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message)
    {
        if (string.Equals(category, captured, StringComparison.Ordinal))
        {
            if (level >= BasinLogLevel.Warn)
            {
                Warnings.Add(message.ToString());
            }

            return;
        }

        forward?.Write(level, category, message);
    }
}
