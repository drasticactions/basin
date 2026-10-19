using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcDiagInput(
    string Kind,
    ulong Window,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AppId,
    double X,
    double Y,
    double LayoutX,
    double LayoutY,
    uint Code,
    bool Pressed,
    double Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail);
