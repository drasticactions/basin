using System.Runtime.CompilerServices;
using System.Text.Unicode;

namespace Basin.Inspector;

/// <summary>
/// The interpolated-string handler for <see cref="InspectorText.Format(ref InspectorFormat)"/>. It writes UTF-8 into the free part of the text buffer and allocates nothing.
/// </summary>
[InterpolatedStringHandler]
public ref struct InspectorFormat
{
    internal Utf8.TryWriteInterpolatedStringHandler Handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="InspectorFormat"/> struct. The compiler calls it for an interpolated string.
    /// </summary>
    /// <param name="literalLength">The number of literal characters in the string.</param>
    /// <param name="formattedCount">The number of holes in the string.</param>
    /// <param name="text">The buffer that receives the UTF-8 bytes.</param>
    /// <param name="shouldAppend">Set to <see langword="false"/> when the handler writes nothing.</param>
    public InspectorFormat(int literalLength, int formattedCount, InspectorText text, out bool shouldAppend)
    {
        Handler = new Utf8.TryWriteInterpolatedStringHandler(literalLength, formattedCount, text.Remaining, out shouldAppend);
    }

    /// <summary>
    /// Writes a literal part of the string.
    /// </summary>
    /// <param name="value">The literal text.</param>
    /// <returns><see langword="true"/> when the text fit in the buffer.</returns>
    public bool AppendLiteral(string value) => Handler.AppendLiteral(value);

    /// <summary>
    /// Writes a value with its default format.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <returns><see langword="true"/> when the text fit in the buffer.</returns>
    public bool AppendFormatted<T>(T value) => Handler.AppendFormatted(value);

    /// <summary>
    /// Writes a value with a format string.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <param name="format">The format string, or <see langword="null"/> for the default format.</param>
    /// <returns><see langword="true"/> when the text fit in the buffer.</returns>
    public bool AppendFormatted<T>(T value, string? format) => Handler.AppendFormatted(value, format);

    /// <summary>
    /// Writes a value padded to a minimum width.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <param name="alignment">The minimum width. A negative value aligns to the left.</param>
    /// <returns><see langword="true"/> when the text fit in the buffer.</returns>
    public bool AppendFormatted<T>(T value, int alignment) => Handler.AppendFormatted(value, alignment);

    /// <summary>
    /// Writes a span of characters as UTF-8.
    /// </summary>
    /// <param name="value">The characters to write.</param>
    /// <returns><see langword="true"/> when the text fit in the buffer.</returns>
    public bool AppendFormatted(scoped ReadOnlySpan<char> value) => Handler.AppendFormatted(value);

    /// <summary>
    /// Writes a string as UTF-8. A <see langword="null"/> string writes nothing.
    /// </summary>
    /// <param name="value">The string to write.</param>
    /// <returns><see langword="true"/> when the text fit in the buffer.</returns>
    public bool AppendFormatted(string? value) => Handler.AppendFormatted(value);
}
