using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcSessionDescription(
    string Compositor,
    string? Backend,
    string? Renderer,
    string? WaylandSocket,
    string? XwaylandDisplay,
    string? IpcSocket,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Outputs = null,
    int Pid = 0);
