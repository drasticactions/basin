namespace Basin.Ipc;

/// <summary>
/// The metadata of a method that <c>ipc/methods</c> with <c>detail</c> returns.
/// </summary>
/// <param name="Description">A sentence that tells what the method does.</param>
/// <param name="ParamsSchema">A JSON Schema object with <c>"type": "object"</c>, or <see langword="null"/> for no schema. The registry stores it compacted.</param>
/// <param name="Traits">The traits that tell a client how safe the method is to call.</param>
public sealed record IpcMethodInfo(
    string Description,
    string? ParamsSchema,
    IpcMethodTraits Traits);
