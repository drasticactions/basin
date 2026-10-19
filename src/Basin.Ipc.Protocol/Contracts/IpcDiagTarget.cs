using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcDiagTarget(
    string Kind,
    string Selector,
    ulong Window,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AppId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Output,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IpcBox? Box);
