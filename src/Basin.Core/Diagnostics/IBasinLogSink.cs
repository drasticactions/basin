namespace Basin.Diagnostics;

public interface IBasinLogSink
{
    void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message);
}
