using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcOptionalBoxConverter : JsonConverter<IpcOptionalBox>
{
    public override bool HandleNull => true;

    public override IpcOptionalBox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null
            ? default
            : new IpcOptionalBox(JsonSerializer.Deserialize(ref reader, IpcJsonContext.Default.IpcBox));

    public override void Write(Utf8JsonWriter writer, IpcOptionalBox value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value.Value is { } box)
        {
            JsonSerializer.Serialize(writer, box, IpcJsonContext.Default.IpcBox);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
