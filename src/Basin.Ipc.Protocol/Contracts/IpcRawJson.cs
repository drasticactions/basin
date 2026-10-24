using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// A JSON value that the contracts carry as UTF-8 bytes and do not parse, for example a method schema.
/// An empty value is written as JSON <see langword="null"/>.
/// </summary>
/// <param name="Utf8">The UTF-8 bytes of one JSON value, or empty for no value.</param>
[JsonConverter(typeof(IpcRawJsonConverter))]
public readonly record struct IpcRawJson(ReadOnlyMemory<byte> Utf8)
{
    /// <summary>
    /// Gets a value indicating whether the value holds no bytes. It is not on the wire.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => Utf8.IsEmpty;

    /// <summary>
    /// Parses the bytes into a new <see cref="JsonElement"/>.
    /// It throws <see cref="JsonException"/> when the value is empty or is not valid JSON.
    /// </summary>
    /// <returns>An element that does not depend on these bytes.</returns>
    public JsonElement ToElement()
    {
        var reader = new Utf8JsonReader(Utf8.Span);
        return JsonElement.ParseValue(ref reader);
    }

    /// <summary>
    /// Decodes the bytes as UTF-8 text.
    /// </summary>
    /// <returns>The JSON text, or an empty string when the value is empty.</returns>
    public override string ToString() => Encoding.UTF8.GetString(Utf8.Span);
}
