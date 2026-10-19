using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcDiagComparison(
    ulong Window,
    bool Passed,
    bool SameSize,
    int Width,
    int Height,
    int ReferenceWidth,
    int ReferenceHeight,
    int Changed,
    int MaxDelta,
    IpcBox ChangedBox,
    int Channel,
    int Pixels,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DiffPath,
    bool TimedOut,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] byte[]? Png = null);
