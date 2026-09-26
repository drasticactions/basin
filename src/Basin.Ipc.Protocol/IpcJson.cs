using System.Text.Json;

namespace Basin.Ipc;

public static class IpcJson
{
    public static ReadOnlySpan<byte> RawToken(ReadOnlySpan<byte> json, scoped ref Utf8JsonReader reader)
    {
        var start = (int)reader.TokenStartIndex;
        return json[start..(int)reader.BytesConsumed];
    }

    public static ReadOnlySpan<byte> RawValue(ReadOnlySpan<byte> json, scoped ref Utf8JsonReader reader)
    {
        var start = (int)reader.TokenStartIndex;
        reader.Skip();
        return json[start..(int)reader.BytesConsumed];
    }

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
