using System.Globalization;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

[InterpolatedStringHandler]
public ref struct LogHandler<T>
    where T : struct, ILogLevelTag
{
    private DefaultInterpolatedStringHandler _inner;
    private char[]? _scratch;

    public LogHandler(int literalLength, int formattedCount, out bool shouldAppend)
    {
        Enabled = shouldAppend = BasinLog.IsEnabled(T.Level);
        if (Enabled)
        {
            _scratch = LogScratch.Take();
            _inner = new DefaultInterpolatedStringHandler(
                literalLength,
                formattedCount,
                CultureInfo.InvariantCulture,
                _scratch);
        }
    }

    public bool Enabled { get; }

    public readonly ReadOnlySpan<char> Text => _inner.Text;

    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    public void AppendFormatted<TValue>(TValue value) => _inner.AppendFormatted(value);

    public void AppendFormatted<TValue>(TValue value, string? format) => _inner.AppendFormatted(value, format);

    public void AppendFormatted(double value) => LogFormat.Append(ref _inner, value, null);

    public void AppendFormatted(double value, string? format) => LogFormat.Append(ref _inner, value, format);

    public void AppendFormatted(float value) => LogFormat.Append(ref _inner, value, null);

    public void AppendFormatted(float value, string? format) => LogFormat.Append(ref _inner, value, format);

    public void AppendFormatted(int value) => LogFormat.Append(ref _inner, (long)value, null);

    public void AppendFormatted(int value, string? format) => LogFormat.Append(ref _inner, (long)value, format);

    public void AppendFormatted(long value) => LogFormat.Append(ref _inner, value, null);

    public void AppendFormatted(long value, string? format) => LogFormat.Append(ref _inner, value, format);

    public void AppendFormatted(uint value) => LogFormat.Append(ref _inner, (ulong)value, null);

    public void AppendFormatted(uint value, string? format) => LogFormat.Append(ref _inner, (ulong)value, format);

    public void AppendFormatted(ulong value) => LogFormat.Append(ref _inner, value, null);

    public void AppendFormatted(ulong value, string? format) => LogFormat.Append(ref _inner, value, format);

    public void AppendFormatted(bool value) => LogFormat.Append(ref _inner, value);

    public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

    public void Clear()
    {
        _inner.Clear();
        if (_scratch is not null)
        {
            LogScratch.Return(_scratch);
            _scratch = null;
        }
    }
}
