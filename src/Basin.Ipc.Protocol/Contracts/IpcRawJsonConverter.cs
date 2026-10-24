using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// Reads any JSON value into an <see cref="IpcRawJson"/> and writes its bytes back unchanged.
/// </summary>
public sealed class IpcRawJsonConverter : JsonConverter<IpcRawJson>
{
    /// <summary>
    /// Gets a value indicating whether the converter receives JSON <see langword="null"/>. It is always <see langword="true"/>.
    /// </summary>
    public override bool HandleNull => true;

    /// <summary>
    /// Copies the next JSON value as compact UTF-8. JSON <see langword="null"/> reads as an empty value.
    /// </summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="typeToConvert">The type to read. The converter ignores it.</param>
    /// <param name="options">The serializer options. The converter ignores them.</param>
    /// <returns>The value as UTF-8 bytes, or an empty value for JSON <see langword="null"/>.</returns>
    public override IpcRawJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return new IpcRawJson(JsonSerializer.SerializeToUtf8Bytes(document.RootElement, IpcJsonContext.Default.JsonElement));
    }

    /// <summary>
    /// Writes the bytes of the value with no validation, or JSON <see langword="null"/> when the value is empty.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write. Its bytes must be valid JSON.</param>
    /// <param name="options">The serializer options. The converter ignores them.</param>
    public override void Write(Utf8JsonWriter writer, IpcRawJson value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value.IsEmpty)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteRawValue(value.Utf8.Span, skipInputValidation: true);
    }
}
