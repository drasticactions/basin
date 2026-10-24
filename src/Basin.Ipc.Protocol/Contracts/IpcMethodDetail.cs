using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// One method in the reply of <c>ipc/methods</c> with <c>detail</c>.
/// </summary>
/// <param name="Name">The method name, in the form <c>namespace/verb</c>.</param>
/// <param name="Description">The description of the method, or <see langword="null"/> for a method with no info.</param>
/// <param name="Schema">The JSON Schema object of the params. It is empty, and written as <see langword="null"/>, for a method with no info.</param>
/// <param name="ReadOnly"><see langword="true"/> when the method does not change the session.</param>
/// <param name="Destructive"><see langword="true"/> when the change loses something that the caller cannot restore.</param>
/// <param name="Idempotent"><see langword="true"/> when a second identical call changes nothing more.</param>
/// <param name="Line">The first stdin line form of the method, or <see langword="null"/> when it has none.</param>
public readonly record struct IpcMethodDetail(
    string Name,
    string? Description,
    IpcRawJson Schema,
    bool ReadOnly,
    bool Destructive,
    bool Idempotent,
    string? Line)
{
    /// <summary>
    /// Gets a value indicating whether the method carries info. It is not on the wire.
    /// </summary>
    [JsonIgnore]
    public bool HasInfo => Description is not null;

    /// <summary>
    /// Gets a value indicating whether <see cref="Schema"/> holds a schema. It is not on the wire.
    /// </summary>
    [JsonIgnore]
    public bool HasSchema => !Schema.IsEmpty;
}
