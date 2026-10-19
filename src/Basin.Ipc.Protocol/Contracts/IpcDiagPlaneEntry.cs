using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcDiagPlaneEntry(
    string Path,
    IpcBox Box,
    bool OnPlane,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);
