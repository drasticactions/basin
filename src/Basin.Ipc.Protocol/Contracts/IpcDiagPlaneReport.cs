using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The plane offload state of one output, in the reply of <c>diag/planes-state</c> and in the <c>diag/plane</c> event.
/// </summary>
/// <param name="Output">The output name.</param>
/// <param name="Skipped">The reason that the output did not try offload, or <see langword="null"/> when it tried.</param>
/// <param name="Entries">One decision for each candidate node: the nodes on a plane first, then the declined nodes.</param>
public readonly record struct IpcDiagPlaneReport(
    string Output,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Skipped,
    IpcDiagPlaneEntry[] Entries);
