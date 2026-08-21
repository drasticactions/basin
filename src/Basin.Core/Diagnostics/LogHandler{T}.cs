using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

[InterpolatedStringHandler]
public ref struct LogHandler<T>
    where T : struct, ILogLevelTag
{
    private DefaultInterpolatedStringHandler _inner;

    public LogHandler(int literalLength, int formattedCount, out bool shouldAppend)
    {
        Enabled = shouldAppend = BasinLog.IsEnabled(T.Level);
        if (Enabled)
        {
            _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        }
    }

    public bool Enabled { get; }

    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    public void AppendFormatted<TValue>(TValue value) => _inner.AppendFormatted(value);

    public void AppendFormatted<TValue>(TValue value, string? format) => _inner.AppendFormatted(value, format);

    public string ToStringAndClear() => _inner.ToStringAndClear();
}
