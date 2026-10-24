using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

/// <summary>
/// The params of one call over their raw JSON bytes. A getter that finds a missing key or a wrong type sets <c>invalid_params</c> on the reply.
/// </summary>
public ref struct IpcParams
{
    private readonly ReadOnlySpan<byte> _json;
    private readonly IpcReply _reply;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcParams"/> struct.
    /// </summary>
    /// <param name="json">The raw JSON of the params. Empty means no params.</param>
    /// <param name="reply">The reply that takes the errors.</param>
    public IpcParams(ReadOnlySpan<byte> json, IpcReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        _json = json;
        _reply = reply;
    }

    /// <summary>
    /// Gets the raw JSON of the params. It is <c>{}</c> when the call had no params.
    /// </summary>
    public readonly ReadOnlySpan<byte> Raw => _json.IsEmpty ? "{}"u8 : _json;

    /// <summary>
    /// Gets a value indicating whether the reply already carries an error. A handler checks it before it acts.
    /// </summary>
    public readonly bool Failed => _reply.IsError;

    /// <summary>
    /// Tells whether a key is present with a value that is not <c>null</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns><see langword="true"/> when the key has a value.</returns>
    public readonly bool Has(string name) => Find(name, out _);

    /// <summary>
    /// Deserializes the params into a params contract. On a missing required key or a wrong type, it sets <c>invalid_params</c> on the reply.
    /// </summary>
    /// <typeparam name="T">The params contract type.</typeparam>
    /// <param name="info">The source-generated metadata of <typeparamref name="T"/>.</param>
    /// <returns>The params, or <see langword="null"/> on an error. Library params are reused, so a deferred handler copies what it needs.</returns>
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

            _reply.Error(IpcErrorCodes.InvalidParams, "'params' must be an object.");
        }
        catch (JsonException exception)
        {
            _reply.Error(IpcErrorCodes.InvalidParams, IpcParamsError.Describe(exception));
        }

        return null;
    }

    /// <summary>
    /// Reads a required string. When the key is absent or not a string, it sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>The value, or an empty string on an error.</returns>
    public readonly string GetString(string name) =>
        TryGetString(name, out var value) ? value : Missing<string>(name, string.Empty);

    /// <summary>
    /// Reads an optional string. An absent or <c>null</c> key is not an error. A value of another type sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">The value, or an empty string when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the key holds a string.</returns>
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

    /// <summary>
    /// Reads a required integer. When the key is absent or not an integer, it sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>The value, or 0 on an error.</returns>
    public readonly long GetInt(string name) => TryGetInt(name, out var value) ? value : Missing(name, 0L);

    /// <summary>
    /// Reads an optional integer. An absent or <c>null</c> key is not an error. A value that is not a 64-bit integer sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">The value, or 0 when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the key holds an integer.</returns>
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

    /// <summary>
    /// Reads a required unsigned integer, as a number or as a string of its decimal digits. When the key is absent or not valid, it sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>The value, or 0 on an error.</returns>
    public readonly ulong GetUlong(string name) => TryGetUlong(name, out var value) ? value : Missing(name, 0UL);

    /// <summary>
    /// Reads an optional unsigned integer, as a number or as a string of its decimal digits. A string lets a JavaScript client send an id above 2^53.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">The value, or 0 when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the key holds a valid value. An invalid value sets <c>invalid_params</c>.</returns>
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

    /// <summary>
    /// Reads a required finite number. When the key is absent or not valid, it sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>The value, or 0 on an error.</returns>
    public readonly double GetDouble(string name) => TryGetDouble(name, out var value) ? value : Missing(name, 0d);

    /// <summary>
    /// Reads an optional finite number. An absent or <c>null</c> key is not an error. Another type sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">The value, or 0 when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the key holds a finite number.</returns>
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

    /// <summary>
    /// Reads a required boolean. When the key is absent or not a boolean, it sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>The value, or <see langword="false"/> on an error.</returns>
    public readonly bool GetBool(string name) => TryGetBool(name, out var value) ? value : Missing(name, false);

    /// <summary>
    /// Reads an optional boolean. An absent or <c>null</c> key is not an error. Another type sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">The value, or <see langword="false"/> when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the key holds a boolean.</returns>
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

    /// <summary>
    /// Reads a required array of strings. When the key is absent or not valid, it sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>A new array, or an empty array on an error.</returns>
    public readonly string[] GetStringArray(string name) =>
        TryGetStringArray(name, out var value) ? value : Missing(name, Array.Empty<string>());

    /// <summary>
    /// Reads an optional array of strings. An absent or <c>null</c> key is not an error. Another shape sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">A new array, or an empty array when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the key holds an array of strings.</returns>
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

    /// <summary>
    /// Reads an optional object whose values are strings. An absent or <c>null</c> key is not an error. Another shape sets <c>invalid_params</c>.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">A new dictionary with ordinal keys. It can hold some entries when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the key holds an object of strings.</returns>
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

    /// <summary>
    /// Finds the raw JSON of a value, of any type. A <c>null</c> value is found as <c>null</c>. It never sets an error.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <param name="value">The raw JSON of the value, a slice of the params.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
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

    /// <summary>
    /// Sets <c>invalid_params</c> on the reply. The first error on a reply wins.
    /// </summary>
    /// <param name="message">The error text.</param>
    public readonly void Invalid(string message) => _reply.Error(IpcErrorCodes.InvalidParams, message);

    private readonly T Missing<T>(string name, T fallback)
    {
        if (!_reply.IsError)
        {
            _reply.Error(IpcErrorCodes.InvalidParams, $"'{name}' is required.");
        }

        return fallback;
    }

    private readonly bool WrongType(string name, string expected)
    {
        _reply.Error(IpcErrorCodes.InvalidParams, $"'{name}' must be {expected}.");
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
