using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// One target in the reply of <c>diag/targets</c>.
/// </summary>
/// <param name="Kind"><c>window</c>, <c>x11</c>, <c>popup</c> or <c>layer</c>, or <c>decorated</c> from a parts source.</param>
/// <param name="Selector">The text that names this target to the <c>diag</c> methods.</param>
/// <param name="Window">The id of the window that owns the target, or 0 for a layer surface or a decorated window.</param>
/// <param name="AppId">The app id of that window, or <see langword="null"/>.</param>
/// <param name="Title">The title of that window, or <see langword="null"/>.</param>
/// <param name="Output">The name of the output under the center of <paramref name="Box"/>, or <see langword="null"/>.</param>
/// <param name="Box">The current bounds in scene coordinates, or <see langword="null"/> when the scene does not show the target.</param>
public readonly record struct IpcDiagTarget(
    string Kind,
    string Selector,
    ulong Window,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AppId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Output,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IpcBox? Box);
