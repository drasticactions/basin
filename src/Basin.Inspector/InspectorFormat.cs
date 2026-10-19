using System.Runtime.CompilerServices;
using System.Text.Unicode;

namespace Basin.Inspector;

[InterpolatedStringHandler]
public ref struct InspectorFormat
{
    internal Utf8.TryWriteInterpolatedStringHandler Handler;

    public InspectorFormat(int literalLength, int formattedCount, InspectorText text, out bool shouldAppend)
    {
        Handler = new Utf8.TryWriteInterpolatedStringHandler(literalLength, formattedCount, text.Remaining, out shouldAppend);
    }

    public bool AppendLiteral(string value) => Handler.AppendLiteral(value);

    public bool AppendFormatted<T>(T value) => Handler.AppendFormatted(value);

    public bool AppendFormatted<T>(T value, string? format) => Handler.AppendFormatted(value, format);

    public bool AppendFormatted<T>(T value, int alignment) => Handler.AppendFormatted(value, alignment);

    public bool AppendFormatted(scoped ReadOnlySpan<char> value) => Handler.AppendFormatted(value);

    public bool AppendFormatted(string? value) => Handler.AppendFormatted(value);
}
