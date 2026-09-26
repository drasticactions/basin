using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcCaptureToConverter : JsonConverter<IpcCaptureTo>
{
    private const string Shape = "'to' is {\"path\": ...}, \"fd\" or \"inline\"";

    public override IpcCaptureTo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.ValueTextEquals("fd"u8) ? IpcCaptureTo.Fd
                : reader.ValueTextEquals("inline"u8) ? IpcCaptureTo.Inline
                : throw new JsonException(Shape);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(Shape);
        }

        string? path = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var isPath = reader.ValueTextEquals("path"u8);
            _ = reader.Read();
            if (isPath && reader.TokenType == JsonTokenType.String)
            {
                path = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return path is null ? throw new JsonException(Shape) : IpcCaptureTo.ToPath(path);
    }

    public override void Write(Utf8JsonWriter writer, IpcCaptureTo value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        switch (value.Kind)
        {
            case IpcCaptureKind.Fd:
                writer.WriteStringValue("fd"u8);
                break;
            case IpcCaptureKind.Path:
                writer.WriteStartObject();
                writer.WriteString("path"u8, value.Path);
                writer.WriteEndObject();
                break;
            default:
                writer.WriteStringValue("inline"u8);
                break;
        }
    }
}
