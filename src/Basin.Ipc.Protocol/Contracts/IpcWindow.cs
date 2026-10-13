using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcWindow(
    ulong Id,
    string Title,
    string AppId,
    IpcWindowState State,
    IpcBox Geometry,
    IpcBox ClientGeometry,
    uint Pid,
    ulong? Parent,
    string? Output,
    ulong? Workspace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? X11Display = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? X11Density = null)
{
    public const string NativeDensity = "native";

    public const string LowDensity = "low";
}
