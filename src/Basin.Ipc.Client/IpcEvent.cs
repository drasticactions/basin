using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

/// <summary>
/// One event that a subscription received.
/// </summary>
/// <param name="Name">The event name, for example <c>window/changed</c>.</param>
/// <param name="Data">The UTF-8 JSON of the event's <c>data</c>.</param>
public sealed record IpcEvent(string Name, byte[] Data)
{
    /// <summary>
    /// Deserializes the event's data into a contract.
    /// </summary>
    /// <typeparam name="T">The event contract type.</typeparam>
    /// <param name="info">The source-generated metadata of <typeparamref name="T"/>.</param>
    /// <returns>The data. It is <see langword="null"/> when the data is the JSON <c>null</c>.</returns>
    public T Read<T>(JsonTypeInfo<T> info) => JsonSerializer.Deserialize(Data, info)!;

    /// <summary>
    /// Formats the event as its name, a space and its data JSON.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"{Name} {System.Text.Encoding.UTF8.GetString(Data)}";
}
