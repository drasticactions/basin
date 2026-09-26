using System.Text.Json;

namespace Basin.Ipc;

public readonly ref struct IpcRequest
{
    private IpcRequest(ReadOnlySpan<byte> id, string? method, ReadOnlySpan<byte> parameters)
    {
        Id = id;
        Method = method;
        Params = parameters;
    }

    public ReadOnlySpan<byte> Id { get; }

    public string? Method { get; }

    public ReadOnlySpan<byte> Params { get; }

    public static bool TryParse(ReadOnlySpan<byte> frame, out IpcRequest request, out string? error)
    {
        ReadOnlySpan<byte> id = default;
        ReadOnlySpan<byte> parameters = default;
        string? method = null;
        error = null;

        try
        {
            var reader = new Utf8JsonReader(frame, new JsonReaderOptions { MaxDepth = 64 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                error = "a request is a JSON object";
                request = default;
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    if (reader.TokenType is JsonTokenType.Number or JsonTokenType.String)
                    {
                        id = IpcJson.RawToken(frame, ref reader);
                    }
                    else if (reader.TokenType != JsonTokenType.Null)
                    {
                        error ??= "'id' is a number or a string";
                        reader.Skip();
                    }
                }
                else if (reader.ValueTextEquals("method"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        method = reader.GetString();
                    }
                    else
                    {
                        error ??= "'method' is a string";
                        reader.Skip();
                    }
                }
                else if (reader.ValueTextEquals("params"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        parameters = IpcJson.RawValue(frame, ref reader);
                    }
                    else if (reader.TokenType != JsonTokenType.Null)
                    {
                        error ??= "'params' is an object";
                        reader.Skip();
                    }
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            while (reader.Read())
            {
            }
        }
        catch (JsonException exception)
        {
            error = $"the frame is not JSON: {exception.Message}";
        }

        if (error is null && method is null)
        {
            error = "a request names a 'method'";
        }

        request = new IpcRequest(id, method, parameters);
        return error is null;
    }
}
