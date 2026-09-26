using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcButtonConverter : JsonConverter<IpcButton>
{
    public override IpcButton Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && IpcButton.TryParse(reader.GetString()!, out var named))
        {
            return named;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt32(out var code))
        {
            return new IpcButton(code);
        }

        throw new JsonException("'button' is left, right, middle, side, extra or an evdev code");
    }

    public override void Write(Utf8JsonWriter writer, IpcButton value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value.Code);
    }
}
