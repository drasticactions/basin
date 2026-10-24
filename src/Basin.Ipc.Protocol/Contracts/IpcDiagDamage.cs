using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The data of the <c>diag/damage</c> event: one scene damage.
/// </summary>
/// <param name="Path">The scene path of the node that caused the damage, or <c>(scene)</c> when no node did.</param>
/// <param name="Box">The damaged box in scene coordinates.</param>
/// <param name="Stack">The stack trace of the first damage from this path, when the subscriber asked for stacks. Otherwise <see langword="null"/>.</param>
public readonly record struct IpcDiagDamage(
    string Path,
    IpcBox Box,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stack);
