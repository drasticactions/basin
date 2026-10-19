using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcDiagDamage(
    string Path,
    IpcBox Box,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stack);
