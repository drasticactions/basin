using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcDiagPlaneReport(
    string Output,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Skipped,
    IpcDiagPlaneEntry[] Entries);
