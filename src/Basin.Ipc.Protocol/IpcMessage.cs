using System.Text.Json;

namespace Basin.Ipc;

/// <summary>
/// One message that a client reads from the control socket: a result, an error or an event. Its spans point into the frame that it was parsed from.
/// </summary>
public readonly ref struct IpcMessage
{
    private IpcMessage(
        IpcMessageKind kind, ReadOnlySpan<byte> id, ReadOnlySpan<byte> payload, string? name, string? message)
    {
        Kind = kind;
        Id = id;
        Payload = payload;
        Name = name;
        ErrorMessage = message;
    }

    /// <summary>
    /// Gets the kind of the message, from the <c>result</c>, <c>error</c> or <c>event</c> key.
    /// </summary>
    public IpcMessageKind Kind { get; }

    /// <summary>
    /// Gets the raw bytes of the <c>id</c> that the server echoed. A string id keeps its quotes. It is empty when the message has no id.
    /// </summary>
    public ReadOnlySpan<byte> Id { get; }

    /// <summary>
    /// Gets the raw JSON of the <c>result</c> of a reply or the <c>data</c> of an event. It is empty when the key is absent.
    /// </summary>
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>
    /// Gets the event name of an event or the error code of an error. It is <see langword="null"/> for a result.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the <c>message</c> of an error, or <see langword="null"/> when the error has none or the message is not an error.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the error code, one of <see cref="IpcErrorCodes"/> from a library method, or <see langword="null"/> when the message is not an error.
    /// </summary>
    public string? ErrorCode => Kind == IpcMessageKind.Error ? Name : null;

    /// <summary>
    /// Gets the event name, or <see langword="null"/> when the message is not an event.
    /// </summary>
    public string? EventName => Kind == IpcMessageKind.Event ? Name : null;

    /// <summary>
    /// Parses the JSON of one frame, without its length header. Unknown keys are skipped.
    /// </summary>
    /// <param name="frame">The frame body. It must stay alive while <paramref name="message"/> is used.</param>
    /// <param name="message">The parsed message, or the default value on failure.</param>
    /// <returns><see langword="false"/> when the frame is not a JSON object or has none of <c>result</c>, <c>error</c> and <c>event</c>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> frame, out IpcMessage message)
    {
        ReadOnlySpan<byte> id = default;
        ReadOnlySpan<byte> payload = default;
        IpcMessageKind? kind = null;
        string? name = null;
        string? text = null;

        try
        {
            var reader = new Utf8JsonReader(frame, new JsonReaderOptions { MaxDepth = 256 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                message = default;
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    id = IpcJson.RawToken(frame, ref reader);
                }
                else if (reader.ValueTextEquals("result"u8))
                {
                    reader.Read();
                    kind = IpcMessageKind.Result;
                    payload = IpcJson.RawValue(frame, ref reader);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    reader.Read();
                    payload = IpcJson.RawValue(frame, ref reader);
                }
                else if (reader.ValueTextEquals("event"u8))
                {
                    reader.Read();
                    kind = IpcMessageKind.Event;
                    name = reader.GetString();
                }
                else if (reader.ValueTextEquals("error"u8))
                {
                    reader.Read();
                    kind = IpcMessageKind.Error;
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                    {
                        if (reader.ValueTextEquals("code"u8))
                        {
                            reader.Read();
                            name = reader.GetString();
                        }
                        else if (reader.ValueTextEquals("message"u8))
                        {
                            reader.Read();
                            text = reader.GetString();
                        }
                        else
                        {
                            reader.Read();
                            reader.Skip();
                        }
                    }
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            message = default;
            return false;
        }
        catch (InvalidOperationException)
        {
            message = default;
            return false;
        }

        if (kind is not { } found)
        {
            message = default;
            return false;
        }

        message = new IpcMessage(found, id, payload, name, text);
        return true;
    }
}
