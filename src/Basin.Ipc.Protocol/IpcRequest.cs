using System.Text.Json;

namespace Basin.Ipc;

/// <summary>
/// One request that the server reads from the control socket. Its spans point into the frame that it was parsed from.
/// </summary>
public readonly ref struct IpcRequest
{
    private IpcRequest(ReadOnlySpan<byte> id, string? method, ReadOnlySpan<byte> parameters)
    {
        Id = id;
        Method = method;
        Params = parameters;
    }

    /// <summary>
    /// Gets the raw bytes of the <c>id</c>, a number or a string with its quotes. It is empty when the request has no id or a null id.
    /// </summary>
    public ReadOnlySpan<byte> Id { get; }

    /// <summary>
    /// Gets the method name, or <see langword="null"/> when the request names none.
    /// </summary>
    public string? Method { get; }

    /// <summary>
    /// Gets the raw JSON object of <c>params</c>. It is empty when <c>params</c> is absent or null.
    /// </summary>
    public ReadOnlySpan<byte> Params { get; }

    /// <summary>
    /// Parses the JSON of one request frame, without its length header. Unknown keys are skipped.
    /// </summary>
    /// <param name="frame">The frame body. It must stay alive while <paramref name="request"/> is used.</param>
    /// <param name="request">The parsed request. It holds what was read also on failure, so the reply can echo the id.</param>
    /// <param name="error">The first fault as a sentence, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the frame is a JSON object with a string <c>method</c> and well-typed <c>id</c> and <c>params</c>.</returns>
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
