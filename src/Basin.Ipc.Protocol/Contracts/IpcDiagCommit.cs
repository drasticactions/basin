using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcDiagCommit(
    string Kind,
    ulong Window,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AppId,
    int Width,
    int Height,
    string Format,
    IpcBox[] Damage,
    int FrameCallbacks,
    bool Feedback,
    uint TimestampMs);
