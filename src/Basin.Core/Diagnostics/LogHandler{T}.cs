using System.Globalization;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// The interpolated string handler for a log call. It formats into a pooled buffer, and only when the level is enabled.
/// </summary>
/// <typeparam name="T">The tag that gives the level of the message.</typeparam>
[InterpolatedStringHandler]
public ref struct LogHandler<T>
    where T : struct, ILogLevelTag
{
    private DefaultInterpolatedStringHandler _inner;
    private char[]? _scratch;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogHandler{T}"/> struct. The compiler calls it for each log call.
    /// </summary>
    /// <param name="literalLength">The total length of the literal parts.</param>
    /// <param name="formattedCount">The number of interpolation holes.</param>
    /// <param name="shouldAppend">Set to <see langword="false"/> when the level is disabled, so that the compiler skips the appends.</param>
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

    /// <summary>
    /// Gets a value indicating whether the level was enabled when the handler was made.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Gets the text of the log message so far. The span is valid until <see cref="Clear"/>.
    /// </summary>
    public readonly ReadOnlySpan<char> Text => _inner.Text;

    /// <summary>
    /// Appends a literal part of the interpolated string.
    /// </summary>
    /// <param name="value">The literal text.</param>
    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    /// <summary>
    /// Appends a value with its default format and the invariant culture.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="value">The value to append.</param>
    public void AppendFormatted<TValue>(TValue value) => _inner.AppendFormatted(value);

    /// <summary>
    /// Appends a value with a format string and the invariant culture.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="value">The value to append.</param>
    /// <param name="format">The format string, or <see langword="null"/> for the default format.</param>
    public void AppendFormatted<TValue>(TValue value, string? format) => _inner.AppendFormatted(value, format);

    /// <summary>
    /// Appends a double with no allocation.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void AppendFormatted(double value) => LogFormat.Append(ref _inner, value, null);

    /// <summary>
    /// Appends a double with a format string and no allocation.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <param name="format">The format string, or <see langword="null"/> for the default format.</param>
    public void AppendFormatted(double value, string? format) => LogFormat.Append(ref _inner, value, format);

    /// <summary>
    /// Appends a float with no allocation.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void AppendFormatted(float value) => LogFormat.Append(ref _inner, value, null);

    /// <summary>
    /// Appends a float with a format string and no allocation.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <param name="format">The format string, or <see langword="null"/> for the default format.</param>
    public void AppendFormatted(float value, string? format) => LogFormat.Append(ref _inner, value, format);

    /// <summary>
    /// Appends a 32-bit integer with no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void AppendFormatted(int value) => LogFormat.Append(ref _inner, (long)value, null);

    /// <summary>
    /// Appends a 32-bit integer with a format string and no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <param name="format">The format string, or <see langword="null"/> for the default format.</param>
    public void AppendFormatted(int value, string? format) => LogFormat.Append(ref _inner, (long)value, format);

    /// <summary>
    /// Appends a 64-bit integer with no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void AppendFormatted(long value) => LogFormat.Append(ref _inner, value, null);

    /// <summary>
    /// Appends a 64-bit integer with a format string and no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <param name="format">The format string, or <see langword="null"/> for the default format.</param>
    public void AppendFormatted(long value, string? format) => LogFormat.Append(ref _inner, value, format);

    /// <summary>
    /// Appends an unsigned 32-bit integer with no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void AppendFormatted(uint value) => LogFormat.Append(ref _inner, (ulong)value, null);

    /// <summary>
    /// Appends an unsigned 32-bit integer with a format string and no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <param name="format">The format string, or <see langword="null"/> for the default format.</param>
    public void AppendFormatted(uint value, string? format) => LogFormat.Append(ref _inner, (ulong)value, format);

    /// <summary>
    /// Appends an unsigned 64-bit integer with no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void AppendFormatted(ulong value) => LogFormat.Append(ref _inner, value, null);

    /// <summary>
    /// Appends an unsigned 64-bit integer with a format string and no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <param name="format">The format string, or <see langword="null"/> for the default format.</param>
    public void AppendFormatted(ulong value, string? format) => LogFormat.Append(ref _inner, value, format);

    /// <summary>
    /// Appends a Boolean value with no boxing.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void AppendFormatted(bool value) => LogFormat.Append(ref _inner, value);

    /// <summary>
    /// Appends a span of characters.
    /// </summary>
    /// <param name="value">The characters to append.</param>
    public void AppendFormatted(ReadOnlySpan<char> value) => _inner.AppendFormatted(value);

    /// <summary>
    /// Releases the text buffer back to the shared pool. The handler holds no text after this call.
    /// </summary>
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
