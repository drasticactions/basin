using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The plane offload decision for one scene node on one output.
/// </summary>
/// <param name="Path">The scene path of the node.</param>
/// <param name="Box">For a node on a plane, the plane box in output physical pixels. For a declined node, the node box in scene coordinates.</param>
/// <param name="OnPlane">Whether the node is on a plane.</param>
/// <param name="Reason">The reason that the node is not on a plane, or <see langword="null"/> when it is.</param>
public readonly record struct IpcDiagPlaneEntry(
    string Path,
    IpcBox Box,
    bool OnPlane,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);
