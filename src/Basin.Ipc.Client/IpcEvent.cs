using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

public sealed record IpcEvent(string Name, byte[] Data)
{
    public T Read<T>(JsonTypeInfo<T> info) => JsonSerializer.Deserialize(Data, info)!;

    public override string ToString() => $"{Name} {System.Text.Encoding.UTF8.GetString(Data)}";
}
