using System.Text.Json.Serialization;

namespace Basin.Ipc;

[JsonConverter(typeof(IpcOptionalBoxConverter))]
public readonly record struct IpcOptionalBox(IpcBox? Value)
{
    public static implicit operator IpcOptionalBox(IpcBox box) => new(box);
}
