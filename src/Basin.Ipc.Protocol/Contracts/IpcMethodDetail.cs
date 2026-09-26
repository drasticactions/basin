using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcMethodDetail(
    string Name,
    string? Description,
    IpcRawJson Schema,
    bool ReadOnly,
    bool Destructive,
    bool Idempotent,
    string? Line)
{
    [JsonIgnore]
    public bool HasInfo => Description is not null;

    [JsonIgnore]
    public bool HasSchema => !Schema.IsEmpty;
}
