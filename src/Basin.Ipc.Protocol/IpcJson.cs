using System.Text.Json;

namespace Basin.Ipc;

/// <summary>
/// Reads raw JSON slices out of a frame without a copy. A reader must start at offset 0 of the span that it reads.
/// </summary>
public static class IpcJson
{
    /// <summary>
    /// Gets the bytes of the current token. A string token keeps its quotes and escapes.
    /// </summary>
    /// <param name="json">The span that <paramref name="reader"/> reads.</param>
    /// <param name="reader">A reader on the token. It does not move.</param>
    /// <returns>A slice of <paramref name="json"/> from the token start to the end of the token.</returns>
    public static ReadOnlySpan<byte> RawToken(ReadOnlySpan<byte> json, scoped ref Utf8JsonReader reader)
    {
        var start = (int)reader.TokenStartIndex;
        return json[start..(int)reader.BytesConsumed];
    }

    /// <summary>
    /// Gets the bytes of the current value, a whole object or array included, and moves the reader to its end.
    /// </summary>
    /// <param name="json">The span that <paramref name="reader"/> reads.</param>
    /// <param name="reader">A reader on the first token of the value. It ends on the last token of the value.</param>
    /// <returns>A slice of <paramref name="json"/> that holds the value.</returns>
    public static ReadOnlySpan<byte> RawValue(ReadOnlySpan<byte> json, scoped ref Utf8JsonReader reader)
    {
        var start = (int)reader.TokenStartIndex;
        reader.Skip();
        return json[start..(int)reader.BytesConsumed];
    }

    /// <summary>
    /// Finds a property of the top-level object of <paramref name="json"/> by its name. It does not look into nested objects.
    /// </summary>
    /// <param name="json">The JSON text. It must be a complete object.</param>
    /// <param name="name">The UTF-8 property name, without escapes.</param>
    /// <param name="value">The raw bytes of the value, a string with its quotes. It is empty when the property is not found.</param>
    /// <returns><see langword="false"/> when the span is empty, is not an object, or has no such property.</returns>
    /// <exception cref="JsonException">The JSON is malformed before the property is found.</exception>
    public static bool TryFindProperty(ReadOnlySpan<byte> json, ReadOnlySpan<byte> name, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (json.IsEmpty)
        {
            return false;
        }

        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var match = reader.ValueTextEquals(name);
            reader.Read();
            if (match)
            {
                value = reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray
                    ? RawValue(json, ref reader)
                    : RawToken(json, ref reader);
                return true;
            }

            reader.Skip();
        }

        return false;
    }
}
