using System.Text.Json;

namespace Basin.Ipc;

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

    public IpcMessageKind Kind { get; }

    public ReadOnlySpan<byte> Id { get; }

    public ReadOnlySpan<byte> Payload { get; }

    public string? Name { get; }

    public string? ErrorMessage { get; }

    public string? ErrorCode => Kind == IpcMessageKind.Error ? Name : null;

    public string? EventName => Kind == IpcMessageKind.Event ? Name : null;

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
