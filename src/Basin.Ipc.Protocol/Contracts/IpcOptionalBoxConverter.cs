using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// Writes an <see cref="IpcOptionalBox"/> as a box object or as JSON <see langword="null"/>.
/// A converter on the type keeps the serializer on its fast path.
/// </summary>
public sealed class IpcOptionalBoxConverter : JsonConverter<IpcOptionalBox>
{
    /// <summary>
    /// Gets a value indicating whether the converter receives JSON <see langword="null"/>. It is always <see langword="true"/>.
    /// </summary>
    public override bool HandleNull => true;

    /// <summary>
    /// Reads a box object, or reads JSON <see langword="null"/> as an empty <see cref="IpcOptionalBox"/>.
    /// </summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="typeToConvert">The type to read. The converter ignores it.</param>
    /// <param name="options">The serializer options. The converter ignores them.</param>
    /// <returns>The box, or a value whose <see cref="IpcOptionalBox.Value"/> is <see langword="null"/>.</returns>
    public override IpcOptionalBox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null
            ? default
            : new IpcOptionalBox(JsonSerializer.Deserialize(ref reader, IpcJsonContext.Default.IpcBox));

    /// <summary>
    /// Writes the box object, or JSON <see langword="null"/> when the value holds no box.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options. The converter ignores them.</param>
    public override void Write(Utf8JsonWriter writer, IpcOptionalBox value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value.Value is { } box)
        {
            JsonSerializer.Serialize(writer, box, IpcJsonContext.Default.IpcBox);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
