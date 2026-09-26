using System.Text.Json.Serialization;

namespace Basin.Ipc;

public readonly record struct IpcKeymapInfo(
    IReadOnlyList<string>? Names,
    uint Size,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Fd = null);
