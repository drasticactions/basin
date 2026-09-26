using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcRawJsonConverter : JsonConverter<IpcRawJson>
{
    public override bool HandleNull => true;

    public override IpcRawJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return new IpcRawJson(JsonSerializer.SerializeToUtf8Bytes(document.RootElement, IpcJsonContext.Default.JsonElement));
    }

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
