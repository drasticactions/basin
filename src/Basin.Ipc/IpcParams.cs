using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

public ref struct IpcParams
{
    private readonly ReadOnlySpan<byte> _json;
    private readonly IpcReply _reply;

    public IpcParams(ReadOnlySpan<byte> json, IpcReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        _json = json;
        _reply = reply;
    }

    public readonly ReadOnlySpan<byte> Raw => _json.IsEmpty ? "{}"u8 : _json;

    public readonly bool Failed => _reply.IsError;

    public readonly bool Has(string name) => Find(name, out _);

    public readonly T? Read<T>(JsonTypeInfo<T> info)
        where T : class, IIpcParams
    {
        ArgumentNullException.ThrowIfNull(info);
        try
        {
            if (JsonSerializer.Deserialize(Raw, IpcParamsReuse.Resolve(info)) is { } value)
            {
                if (value.Missing is { } missing)
                {
                    _reply.Error(IpcErrorCodes.InvalidParams, missing);
                    return null;
                }

                return value;
            }

            _reply.Error(IpcErrorCodes.InvalidParams, "'params' is an object");
        }
        catch (JsonException exception)
        {
            _reply.Error(IpcErrorCodes.InvalidParams, IpcParamsError.Describe(exception));
        }

        return null;
    }

    public readonly string GetString(string name) =>
        TryGetString(name, out var value) ? value : Missing<string>(name, string.Empty);

    public readonly bool TryGetString(string name, out string value)
    {
        value = string.Empty;
        if (!Find(name, out var reader))
        {
            return false;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            return WrongType(name, "a string");
        }

        value = reader.GetString()!;
        return true;
    }

    public readonly long GetInt(string name) => TryGetInt(name, out var value) ? value : Missing(name, 0L);

    public readonly bool TryGetInt(string name, out long value)
    {
        value = 0;
        if (!Find(name, out var reader))
        {
            return false;
        }

        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out value))
        {
            return WrongType(name, "an integer");
        }

        return true;
    }

    public readonly ulong GetUlong(string name) => TryGetUlong(name, out var value) ? value : Missing(name, 0UL);

    public readonly bool TryGetUlong(string name, out ulong value)
    {
        value = 0;
        if (!Find(name, out var reader))
        {
            return false;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString()!;
            if (text.Length > 0 && text.Length <= 20 && text.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0
                && ulong.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return WrongType(name, "a non-negative integer or a string of its decimal digits");
        }

        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetUInt64(out value))
        {
            return WrongType(name, "a non-negative integer or a string of its decimal digits");
        }

        return true;
    }

    public readonly double GetDouble(string name) => TryGetDouble(name, out var value) ? value : Missing(name, 0d);

    public readonly bool TryGetDouble(string name, out double value)
    {
        value = 0;
        if (!Find(name, out var reader))
        {
            return false;
        }

        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out value) || !double.IsFinite(value))
        {
            return WrongType(name, "a number");
        }

        return true;
    }

    public readonly bool GetBool(string name) => TryGetBool(name, out var value) ? value : Missing(name, false);

    public readonly bool TryGetBool(string name, out bool value)
    {
        value = false;
        if (!Find(name, out var reader))
        {
            return false;
        }

        if (reader.TokenType is not (JsonTokenType.True or JsonTokenType.False))
        {
            return WrongType(name, "a boolean");
        }

        value = reader.TokenType == JsonTokenType.True;
        return true;
    }

    public readonly string[] GetStringArray(string name) =>
        TryGetStringArray(name, out var value) ? value : Missing(name, Array.Empty<string>());

    public readonly bool TryGetStringArray(string name, out string[] value)
    {
        value = [];
        if (!Find(name, out var reader))
        {
            return false;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return WrongType(name, "an array of strings");
        }

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                return WrongType(name, "an array of strings");
            }

            items.Add(reader.GetString()!);
        }

        value = [.. items];
        return true;
    }

    public readonly bool TryGetStringMap(string name, out Dictionary<string, string> value)
    {
        value = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Find(name, out var reader))
        {
            return false;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return WrongType(name, "an object of strings");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var key = reader.GetString()!;
            reader.Read();
            if (reader.TokenType != JsonTokenType.String)
            {
                return WrongType(name, "an object of strings");
            }

            value[key] = reader.GetString()!;
        }

        return true;
    }

    public readonly bool TryGetRaw(string name, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (_json.IsEmpty)
        {
            return false;
        }

        var reader = new Utf8JsonReader(_json);
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
                value = IpcJson.RawValue(_json, ref reader);
                return true;
            }

            reader.Skip();
        }

        return false;
    }

    public readonly void Invalid(string message) => _reply.Error(IpcErrorCodes.InvalidParams, message);

    private readonly T Missing<T>(string name, T fallback)
    {
        if (!_reply.IsError)
        {
            _reply.Error(IpcErrorCodes.InvalidParams, $"'{name}' is required");
        }

        return fallback;
    }

    private readonly bool WrongType(string name, string expected)
    {
        _reply.Error(IpcErrorCodes.InvalidParams, $"'{name}' must be {expected}");
        return false;
    }

    private readonly bool Find(string name, out Utf8JsonReader found)
    {
        found = default;
        if (_json.IsEmpty)
        {
            return false;
        }

        var reader = new Utf8JsonReader(_json);
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
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return false;
                }

                found = reader;
                return true;
            }

            reader.Skip();
        }

        return false;
    }
}
