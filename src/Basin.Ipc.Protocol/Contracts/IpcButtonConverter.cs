using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// Reads a <see cref="IpcButton"/> from a button name or an evdev code, and writes it as the code.
/// </summary>
public sealed class IpcButtonConverter : JsonConverter<IpcButton>
{
    /// <summary>
    /// Reads <c>left</c>, <c>right</c>, <c>middle</c>, <c>side</c>, <c>extra</c> or a non-negative evdev code.
    /// </summary>
    /// <param name="reader">The reader on the value.</param>
    /// <param name="typeToConvert">The converted type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The button.</returns>
    /// <exception cref="JsonException">The value is another string, another number or another token.</exception>
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

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IpcButton value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value.Code);
    }
}
