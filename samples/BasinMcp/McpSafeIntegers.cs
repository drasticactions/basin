using System.Text.Encodings.Web;
using System.Text.Json;

namespace BasinMcp;

internal static class McpSafeIntegers
{
    public const long Limit = 1L << 53;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] Rewrite(byte[] json) => NeedsRewrite(json) ? Copy(json) : json;

    public static bool IsUnsafe(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number
        && reader.ValueSpan.IndexOfAny((byte)'.', (byte)'e', (byte)'E') < 0
        && (reader.TryGetInt64(out var signed) ? signed is > Limit or < -Limit : reader.TryGetUInt64(out _));

    private static bool NeedsRewrite(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        while (reader.Read())
        {
            if (IsUnsafe(ref reader))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] Copy(ReadOnlySpan<byte> json)
    {
        using var stream = new MemoryStream(json.Length + 64);
        var reader = new Utf8JsonReader(json);
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        writer.WriteStartObject();
                        break;
                    case JsonTokenType.EndObject:
                        writer.WriteEndObject();
                        break;
                    case JsonTokenType.StartArray:
                        writer.WriteStartArray();
                        break;
                    case JsonTokenType.EndArray:
                        writer.WriteEndArray();
                        break;
                    case JsonTokenType.PropertyName:
                        writer.WritePropertyName(reader.GetString()!);
                        break;
                    case JsonTokenType.String:
                        writer.WriteStringValue(reader.GetString());
                        break;
                    case JsonTokenType.Number when IsUnsafe(ref reader):
                        writer.WriteStringValue(System.Text.Encoding.UTF8.GetString(reader.ValueSpan));
                        break;
                    case JsonTokenType.Number:
                        writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                        break;
                    case JsonTokenType.True:
                        writer.WriteBooleanValue(true);
                        break;
                    case JsonTokenType.False:
                        writer.WriteBooleanValue(false);
                        break;
                    case JsonTokenType.Null:
                        writer.WriteNullValue();
                        break;
                }
            }
        }

        return stream.ToArray();
    }
}
